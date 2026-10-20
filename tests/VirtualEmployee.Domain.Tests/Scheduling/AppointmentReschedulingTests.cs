using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AppointmentReschedulingTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Reschedule_ShouldChangeDestinationAndPreserveCommercialData()
    {
        var appointment = CreateAppointment();
        var originalId = appointment.Id;
        var originalTenantId = appointment.TenantId;
        var originalBusinessId = appointment.BusinessId;
        var originalCustomerId = appointment.CustomerId;
        var originalItem = Assert.Single(appointment.Items);
        var originalExpiration = appointment.ReservationExpiresAt;

        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var startsAt = CreatedAt.AddDays(1);
        var now = CreatedAt.AddMinutes(2);

        var changed = appointment.Reschedule(
            locationId,
            professionalId,
            startsAt,
            now);

        Assert.True(changed);
        Assert.Equal(locationId, appointment.LocationId);
        Assert.Equal(professionalId, appointment.ProfessionalId);
        Assert.Equal(startsAt, appointment.StartsAt);
        Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
        Assert.Equal(now, appointment.UpdatedAt);

        Assert.Equal(originalId, appointment.Id);
        Assert.Equal(originalTenantId, appointment.TenantId);
        Assert.Equal(originalBusinessId, appointment.BusinessId);
        Assert.Equal(originalCustomerId, appointment.CustomerId);
        Assert.Equal(CreatedAt, appointment.CreatedAt);
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.True(appointment.OccupiesSchedule);
        Assert.Equal(originalExpiration, appointment.ReservationExpiresAt);
        Assert.Null(appointment.CancellationReason);
        Assert.Null(appointment.CancelledAt);

        var item = Assert.Single(appointment.Items);

        Assert.Same(originalItem, item);
        Assert.Equal("Corte", item.ServiceNameSnapshot);
        Assert.Equal(65m, item.PriceSnapshot);
        Assert.Equal(45, item.DurationMinutesSnapshot);
        Assert.Equal(65m, appointment.TotalPriceSnapshot);
        Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
    }

    [Fact]
    public void Reschedule_WithEquivalentDestination_ShouldPreserveAudit()
    {
        var appointment = CreateAppointment();

        var changed = appointment.Reschedule(
            appointment.LocationId,
            appointment.ProfessionalId,
            appointment.StartsAt.ToOffset(TimeSpan.FromHours(-3)),
            CreatedAt.AddMinutes(2));

        Assert.False(changed);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);
        Assert.Equal(
            CreatedAt.AddMinutes(10),
            appointment.ReservationExpiresAt);
    }

    [Fact]
    public void Reschedule_ShouldNormalizeTimestampsToUtc()
    {
        var appointment = CreateAppointment();

        var startsAt = CreatedAt.AddDays(1)
            .ToOffset(TimeSpan.FromHours(-3));

        var now = CreatedAt.AddMinutes(2)
            .ToOffset(TimeSpan.FromHours(-3));

        appointment.Reschedule(
            appointment.LocationId,
            appointment.ProfessionalId,
            startsAt,
            now);

        Assert.Equal(startsAt.ToUniversalTime(), appointment.StartsAt);
        Assert.Equal(TimeSpan.Zero, appointment.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, appointment.EndsAt.Offset);
        Assert.Equal(now.ToUniversalTime(), appointment.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, appointment.UpdatedAt.Offset);
    }

    [Fact]
    public void Reschedule_WhenAnticipated_ShouldShortenButNeverExtendReservation()
    {
        var appointment = CreateAppointment();
        var locationId = appointment.LocationId;
        var professionalId = appointment.ProfessionalId;

        appointment.Reschedule(
            locationId,
            professionalId,
            CreatedAt.AddMinutes(5),
            CreatedAt.AddMinutes(1));

        Assert.Equal(
            CreatedAt.AddMinutes(5),
            appointment.ReservationExpiresAt);
        Assert.Equal(
            CreatedAt.AddMinutes(50),
            appointment.EndsAt);

        appointment.Reschedule(
            locationId,
            professionalId,
            CreatedAt.AddHours(3),
            CreatedAt.AddMinutes(2));

        Assert.Equal(
            CreatedAt.AddMinutes(5),
            appointment.ReservationExpiresAt);
        Assert.Equal(
            CreatedAt.AddHours(3),
            appointment.StartsAt);
        Assert.Equal(
            CreatedAt.AddHours(3).AddMinutes(45),
            appointment.EndsAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("empty-location")]
    [InlineData("empty-professional")]
    [InlineData("past-start")]
    [InlineData("start-at-now")]
    [InlineData("past-update")]
    [InlineData("expiration-boundary")]
    [InlineData("after-expiration")]
    [InlineData("already-started")]
    [InlineData("end-overflow")]
    public void Reschedule_WithInvalidRequest_ShouldPreserveOriginalData(
        string scenario)
    {
        var appointment = CreateAppointment();

        var originalLocation = appointment.LocationId;
        var originalProfessional = appointment.ProfessionalId;
        var originalStart = appointment.StartsAt;
        var originalEnd = appointment.EndsAt;
        var originalExpiration = appointment.ReservationExpiresAt;
        var originalUpdatedAt = appointment.UpdatedAt;
        var originalItem = Assert.Single(appointment.Items);

        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var startsAt = CreatedAt.AddHours(3);
        var now = CreatedAt.AddMinutes(1);

        switch (scenario)
        {
            case "empty-location":
                locationId = Guid.Empty;
                break;

            case "empty-professional":
                professionalId = Guid.Empty;
                break;

            case "past-start":
                startsAt = now.AddMinutes(-1);
                break;

            case "start-at-now":
                startsAt = now;
                break;

            case "past-update":
                now = CreatedAt.AddTicks(-1);
                break;

            case "expiration-boundary":
                now = CreatedAt.AddMinutes(10);
                break;

            case "after-expiration":
                now = CreatedAt.AddMinutes(11);
                break;

            case "already-started":
                now = originalStart;
                break;

            case "end-overflow":
                startsAt = DateTimeOffset.MaxValue.AddMinutes(-10);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var exception = Record.Exception(() =>
            appointment.Reschedule(
                locationId,
                professionalId,
                startsAt,
                now));

        Assert.NotNull(exception);

        if (scenario is "empty-location" or "empty-professional")
        {
            var argumentException = Assert.IsType<ArgumentException>(
                exception);

            Assert.Equal(
                scenario == "empty-location"
                    ? "locationId"
                    : "professionalId",
                argumentException.ParamName);
        }
        else if (scenario is
                 "expiration-boundary" or
                 "after-expiration" or
                 "already-started")
        {
            Assert.IsType<InvalidOperationException>(exception);
        }
        else
        {
            var argumentException =
                Assert.IsType<ArgumentOutOfRangeException>(exception);

            if (scenario != "end-overflow")
            {
                Assert.Equal(
                    scenario == "past-update" ? "now" : "startsAt",
                    argumentException.ParamName);
            }
        }

        Assert.Equal(originalLocation, appointment.LocationId);
        Assert.Equal(originalProfessional, appointment.ProfessionalId);
        Assert.Equal(originalStart, appointment.StartsAt);
        Assert.Equal(originalEnd, appointment.EndsAt);
        Assert.Equal(originalExpiration, appointment.ReservationExpiresAt);
        Assert.Equal(originalUpdatedAt, appointment.UpdatedAt);
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Equal(65m, appointment.TotalPriceSnapshot);
        Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
        Assert.Null(appointment.CancellationReason);
        Assert.Null(appointment.CancelledAt);
        Assert.Same(originalItem, Assert.Single(appointment.Items));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("cancelled-by-client")]
    [InlineData("cancelled-by-business")]
    public void Reschedule_WithTerminalState_ShouldPreserveStateAndAudit(
        string scenario)
    {
        var appointment = CreateAppointment();

        if (scenario == "expired")
        {
            Assert.True(
                appointment.Expire(CreatedAt.AddMinutes(10)));
        }
        else
        {
            appointment.Cancel(
                scenario == "cancelled-by-client"
                    ? AppointmentStatus.CancelledByClient
                    : AppointmentStatus.CancelledByBusiness,
                "Motivo original",
                CreatedAt.AddMinutes(1));
        }

        var originalStatus = appointment.Status;
        var originalLocation = appointment.LocationId;
        var originalProfessional = appointment.ProfessionalId;
        var originalStart = appointment.StartsAt;
        var originalEnd = appointment.EndsAt;
        var originalUpdatedAt = appointment.UpdatedAt;
        var originalCancelledAt = appointment.CancelledAt;
        var originalReason = appointment.CancellationReason;

        Assert.Throws<InvalidOperationException>(() =>
            appointment.Reschedule(
                Guid.NewGuid(),
                Guid.NewGuid(),
                CreatedAt.AddDays(1),
                CreatedAt.AddMinutes(11)));

        Assert.Equal(originalStatus, appointment.Status);
        Assert.Equal(originalLocation, appointment.LocationId);
        Assert.Equal(originalProfessional, appointment.ProfessionalId);
        Assert.Equal(originalStart, appointment.StartsAt);
        Assert.Equal(originalEnd, appointment.EndsAt);
        Assert.Equal(originalUpdatedAt, appointment.UpdatedAt);
        Assert.Equal(originalCancelledAt, appointment.CancelledAt);
        Assert.Equal(originalReason, appointment.CancellationReason);
        Assert.Equal(65m, appointment.TotalPriceSnapshot);
        Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(30)]
    public void Reschedule_ShouldCapReservationAtEarlierStart(
        int startAfterMinutes)
    {
        var appointment = CreateAppointment();
        var startsAt = CreatedAt.AddMinutes(startAfterMinutes);

        appointment.Reschedule(
            appointment.LocationId,
            appointment.ProfessionalId,
            startsAt,
            CreatedAt.AddMinutes(1));

        Assert.Equal(
            CreatedAt.AddMinutes(Math.Min(10, startAfterMinutes)),
            appointment.ReservationExpiresAt);

        Assert.Equal(startsAt, appointment.StartsAt);
        Assert.Equal(startsAt.AddMinutes(45), appointment.EndsAt);
    }

    #endregion

    #region Helpers

    private static Appointment CreateAppointment()
    {
        var tenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        return new Appointment(
            appointmentId,
            tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreatedAt.AddHours(2),
            CreatedAt,
            TimeSpan.FromMinutes(10),
            [
                new AppointmentItem(
                    Guid.NewGuid(),
                    tenantId,
                    appointmentId,
                    Guid.NewGuid(),
                    "Corte",
                    65m,
                    45)
            ]);
    }

    #endregion
}