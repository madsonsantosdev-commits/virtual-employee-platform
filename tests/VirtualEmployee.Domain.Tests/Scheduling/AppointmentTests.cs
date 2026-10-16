using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AppointmentTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Constructor_WithZeroTotalPrice_ShouldAccept()
    {
        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var appointment = CreateAppointment(
            appointmentId,
            tenantId,
            [CreateItem(appointmentId, tenantId, 0m, 30)]);

        Assert.Equal(0m, appointment.TotalPriceSnapshot);
        Assert.Equal(30, appointment.TotalDurationMinutesSnapshot);
        Assert.Equal(
            appointment.StartsAt.AddMinutes(30),
            appointment.EndsAt);
    }

    [Fact]
    public void Constructor_WithPriceOverflow_ShouldReject()
    {
        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        Assert.Throws<OverflowException>(() =>
            CreateAppointment(
                appointmentId,
                tenantId,
                [
                    CreateItem(appointmentId, tenantId, decimal.MaxValue, 30),
                    CreateItem(appointmentId, tenantId, 1m, 30)
                ]));
    }

    [Fact]
    public void Constructor_WithDurationOverflow_ShouldReject()
    {
        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAppointment(
                appointmentId,
                tenantId,
                [
                    CreateItem(appointmentId, tenantId, 65m, int.MaxValue),
                    CreateItem(appointmentId, tenantId, 35m, 1)
                ]));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithEndBeyondSupportedDate_ShouldReject()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAppointment(
                startsAt: DateTimeOffset.MaxValue.AddMinutes(-1)));
    }

    [Fact]
    public void Constructor_WithNullItems_ShouldReject()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Appointment(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                CreatedAt.AddHours(2),
                CreatedAt,
                TimeSpan.FromMinutes(10),
                null!));

        Assert.Equal("items", exception.ParamName);
    }

    [Fact]
    public void Constructor_ShouldCalculateTotalsAndCreatePendingReservation()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(2);

        var firstItem = CreateItem(id, tenantId, 65m, 45);
        var secondItem = CreateItem(id, tenantId, 35m, 20);

        var appointment = new Appointment(
            id,
            tenantId,
            businessId,
            locationId,
            customerId,
            professionalId,
            startsAt.ToOffset(TimeSpan.FromHours(-3)),
            CreatedAt.ToOffset(TimeSpan.FromHours(-3)),
            TimeSpan.FromMinutes(10),
            [firstItem, secondItem]);

        Assert.Equal(id, appointment.Id);
        Assert.Equal(tenantId, appointment.TenantId);
        Assert.Equal(businessId, appointment.BusinessId);
        Assert.Equal(locationId, appointment.LocationId);
        Assert.Equal(customerId, appointment.CustomerId);
        Assert.Equal(professionalId, appointment.ProfessionalId);

        Assert.Equal(100m, appointment.TotalPriceSnapshot);
        Assert.Equal(65, appointment.TotalDurationMinutesSnapshot);
        Assert.Equal(startsAt, appointment.StartsAt);
        Assert.Equal(startsAt.AddMinutes(65), appointment.EndsAt);

        Assert.Equal(TimeSpan.Zero, appointment.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, appointment.EndsAt.Offset);
        Assert.Equal(TimeSpan.Zero, appointment.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, appointment.UpdatedAt.Offset);

        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.True(appointment.OccupiesSchedule);
        Assert.Equal<DateTimeOffset?>(
            CreatedAt.AddMinutes(10),
            appointment.ReservationExpiresAt);

        Assert.Equal(CreatedAt, appointment.CreatedAt);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);

        Assert.Collection(
            appointment.Items,
            item => Assert.Same(firstItem, item),
            item => Assert.Same(secondItem, item));
    }

    [Fact]
    public void Constructor_ShouldCopyInputCollection()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var items = new List<AppointmentItem>
        {
            CreateItem(id, tenantId, 65m, 45)
        };

        var appointment = CreateAppointment(id, tenantId, items);

        items.Clear();

        Assert.Single(appointment.Items);
        Assert.Equal(65m, appointment.TotalPriceSnapshot);
        Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
    }

    [Fact]
    public void Expire_WhenRepeated_ShouldPreserveUpdatedAt()
    {
        var appointment = CreateAppointment();
        var expiration = CreatedAt.AddMinutes(10);

        Assert.True(appointment.Expire(expiration));

        Assert.False(appointment.Expire(expiration.AddMinutes(5)));
        Assert.Equal(AppointmentStatus.Expired, appointment.Status);
        Assert.False(appointment.OccupiesSchedule);
        Assert.Equal(expiration, appointment.UpdatedAt);
        Assert.Equal(CreatedAt, appointment.CreatedAt);

        Assert.Equal<DateTimeOffset?>(
            expiration,
            appointment.ReservationExpiresAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("empty")]
    [InlineData("null-item")]
    [InlineData("other-tenant")]
    [InlineData("other-appointment")]
    [InlineData("duplicate-item")]
    [InlineData("duplicate-service")]
    public void Constructor_WithInvalidItems_ShouldReject(string scenario)
    {
        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var firstItem = CreateItem(appointmentId, tenantId, 65m, 45);

        AppointmentItem[] items = scenario switch
        {
            "empty" => [],

            "null-item" => [null!],

            "other-tenant" =>
            [
                CreateItem(appointmentId, Guid.NewGuid(), 65m, 45)
            ],

            "other-appointment" =>
            [
                CreateItem(Guid.NewGuid(), tenantId, 65m, 45)
            ],

            "duplicate-item" =>
            [
                firstItem,
                new AppointmentItem(
                    firstItem.Id,
                    tenantId,
                    appointmentId,
                    Guid.NewGuid(),
                    "Other Service",
                    35m,
                    20)
            ],

            "duplicate-service" =>
            [
                firstItem,
                new AppointmentItem(
                    Guid.NewGuid(),
                    tenantId,
                    appointmentId,
                    firstItem.ServiceId,
                    "Same Service",
                    65m,
                    45)
            ],

            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var exception = Assert.Throws<ArgumentException>(() =>
            CreateAppointment(appointmentId, tenantId, items));

        Assert.Equal("items", exception.ParamName);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("tenantId")]
    [InlineData("businessId")]
    [InlineData("locationId")]
    [InlineData("customerId")]
    [InlineData("professionalId")]
    public void Constructor_WithEmptyIdentity_ShouldReject(string parameter)
    {
        var appointmentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var exception = Assert.Throws<ArgumentException>(() =>
            new Appointment(
                parameter == "id" ? Guid.Empty : appointmentId,
                parameter == "tenantId" ? Guid.Empty : tenantId,
                parameter == "businessId" ? Guid.Empty : Guid.NewGuid(),
                parameter == "locationId" ? Guid.Empty : Guid.NewGuid(),
                parameter == "customerId" ? Guid.Empty : Guid.NewGuid(),
                parameter == "professionalId" ? Guid.Empty : Guid.NewGuid(),
                CreatedAt.AddHours(2),
                CreatedAt,
                TimeSpan.FromMinutes(10),
                [CreateItem(appointmentId, tenantId, 65m, 45)]));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Constructor_WithStartNotAfterCreation_ShouldReject(
        int minutesAfterCreation)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAppointment(
                startsAt: CreatedAt.AddMinutes(minutesAfterCreation)));

        Assert.Equal("startsAt", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Constructor_WithInvalidReservationLifetime_ShouldReject(
        int lifetimeMinutes)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateAppointment(
                reservationLifetime: TimeSpan.FromMinutes(lifetimeMinutes)));

        Assert.Equal("reservationLifetime", exception.ParamName);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(30, 10)]
    public void Constructor_ShouldLimitReservationToAppointmentStart(
        int lifetimeMinutes,
        int expectedExpirationMinutes)
    {
        var appointment = CreateAppointment(
            startsAt: CreatedAt.AddMinutes(10),
            reservationLifetime: TimeSpan.FromMinutes(lifetimeMinutes));

        Assert.Equal<DateTimeOffset?>(
            CreatedAt.AddMinutes(expectedExpirationMinutes),
            appointment.ReservationExpiresAt);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Expire_ShouldRespectExpirationBoundary(
        int secondsFromExpiration,
        bool expectedExpired)
    {
        var appointment = CreateAppointment();

        var now = CreatedAt
            .AddMinutes(10)
            .AddSeconds(secondsFromExpiration)
            .ToOffset(TimeSpan.FromHours(-3));

        var changed = appointment.Expire(now);

        Assert.Equal(expectedExpired, changed);
        Assert.Equal(
            expectedExpired
                ? AppointmentStatus.Expired
                : AppointmentStatus.Pending,
            appointment.Status);

        Assert.Equal(!expectedExpired, appointment.OccupiesSchedule);

        Assert.Equal(
            expectedExpired ? now.ToUniversalTime() : CreatedAt,
            appointment.UpdatedAt);

        Assert.Equal(TimeSpan.Zero, appointment.UpdatedAt.Offset);
    }

    #endregion

    #region Helpers

    private static Appointment CreateAppointment(
        Guid? id = null,
        Guid? tenantId = null,
        IEnumerable<AppointmentItem>? items = null,
        DateTimeOffset? startsAt = null,
        TimeSpan? reservationLifetime = null)
    {
        var appointmentId = id ?? Guid.NewGuid();
        var ownerTenantId = tenantId ?? Guid.NewGuid();

        return new Appointment(
            appointmentId,
            ownerTenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            startsAt ?? CreatedAt.AddHours(2),
            CreatedAt,
            reservationLifetime ?? TimeSpan.FromMinutes(10),
            items ??
            [
                CreateItem(appointmentId, ownerTenantId, 65m, 45)
            ]);
    }

    private static AppointmentItem CreateItem(
        Guid appointmentId,
        Guid tenantId,
        decimal price,
        int duration)
    {
        return new AppointmentItem(
            Guid.NewGuid(),
            tenantId,
            appointmentId,
            Guid.NewGuid(),
            "Service",
            price,
            duration);
    }

    #endregion
}