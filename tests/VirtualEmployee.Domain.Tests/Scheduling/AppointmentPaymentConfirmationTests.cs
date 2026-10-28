using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AppointmentPaymentConfirmationTests
{
    #region Facts

    [Fact]
    public void ConfirmPayment_ShouldConfirmAndPreserveSnapshots()
    {
        var appointment = CreateAppointment();
        var payment = CreateConfirmedPayment(appointment);
        var now = CreatedAt.AddMinutes(3);

        var originalItem = Assert.Single(appointment.Items);
        var originalStart = appointment.StartsAt;
        var originalEnd = appointment.EndsAt;
        var originalExpiration = appointment.ReservationExpiresAt;

        var changed = appointment.ConfirmPayment(payment, now);

        Assert.True(changed);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(now, appointment.UpdatedAt);
        Assert.Equal(CreatedAt, appointment.CreatedAt);
        Assert.Equal(originalStart, appointment.StartsAt);
        Assert.Equal(originalEnd, appointment.EndsAt);
        Assert.Equal(
            originalExpiration,
            appointment.ReservationExpiresAt);
        Assert.Equal(65m, appointment.TotalPriceSnapshot);
        Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);
        Assert.Same(originalItem, Assert.Single(appointment.Items));
        Assert.Null(appointment.CancelledAt);
        Assert.Null(appointment.CancellationReason);
        Assert.True(appointment.OccupiesSchedule);
    }

    [Fact]
    public void ConfirmPayment_WhenRepeated_ShouldPreserveOriginalAudit()
    {
        var appointment = CreateAppointment();
        var payment = CreateConfirmedPayment(appointment);
        var confirmedAt = CreatedAt.AddMinutes(3);

        Assert.True(
            appointment.ConfirmPayment(payment, confirmedAt));

        var changed = appointment.ConfirmPayment(
            payment,
            CreatedAt.AddHours(1));

        Assert.False(changed);
        Assert.Equal(AppointmentStatus.Confirmed, appointment.Status);
        Assert.Equal(confirmedAt, appointment.UpdatedAt);
    }

    [Fact]
    public void ConfirmPayment_WithPendingPayment_ShouldReject()
    {
        var appointment = CreateAppointment();

        var payment = new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            appointment.ConfirmPayment(
                payment,
                CreatedAt.AddMinutes(3)));

        AssertPendingAndUnchanged(appointment);
    }

    [Fact]
    public void ConfirmPayment_WithNullPayment_ShouldReject()
    {
        var appointment = CreateAppointment();

        Assert.Throws<ArgumentNullException>(() =>
            appointment.ConfirmPayment(
                null!,
                CreatedAt.AddMinutes(3)));

        AssertPendingAndUnchanged(appointment);
    }

    [Fact]
    public void ConfirmPayment_WithCancelledAppointment_ShouldNotReactivate()
    {
        var appointment = CreateAppointment();
        var payment = CreateConfirmedPayment(appointment);
        var cancelledAt = CreatedAt.AddMinutes(3);

        appointment.Cancel(
            AppointmentStatus.CancelledByClient,
            "Cliente solicitou",
            cancelledAt);

        Assert.Throws<InvalidOperationException>(() =>
            appointment.ConfirmPayment(
                payment,
                CreatedAt.AddMinutes(4)));

        Assert.Equal(
            AppointmentStatus.CancelledByClient,
            appointment.Status);
        Assert.Equal(cancelledAt, appointment.UpdatedAt);
        Assert.Equal(cancelledAt, appointment.CancelledAt);
        Assert.Equal(
            "Cliente solicitou",
            appointment.CancellationReason);
    }

    [Fact]
    public void ConfirmPayment_WithExpiredAppointment_ShouldNotReactivate()
    {
        var appointment = CreateAppointment();
        var payment = CreateConfirmedPayment(appointment);
        var expiredAt = CreatedAt.AddMinutes(10);

        Assert.True(appointment.Expire(expiredAt));

        Assert.Throws<InvalidOperationException>(() =>
            appointment.ConfirmPayment(
                payment,
                expiredAt.AddMinutes(1)));

        Assert.Equal(AppointmentStatus.Expired, appointment.Status);
        Assert.Equal(expiredAt, appointment.UpdatedAt);
        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
    }

    [Fact]
    public void ConfirmPayment_WithDifferentAmount_ShouldReject()
    {
        var appointment = CreateAppointment();

        var differentSnapshot = CreateAppointment(
            tenantId: appointment.TenantId,
            businessId: appointment.BusinessId,
            appointmentId: appointment.Id,
            amount: 66m);

        var payment = CreateConfirmedPayment(differentSnapshot);

        Assert.Throws<ArgumentException>(() =>
            appointment.ConfirmPayment(
                payment,
                CreatedAt.AddMinutes(3)));

        AssertPendingAndUnchanged(appointment);
    }

    [Fact]
    public void ConfirmPayment_WithOffset_ShouldStoreUtc()
    {
        var appointment = CreateAppointment();
        var payment = CreateConfirmedPayment(appointment);

        var now = CreatedAt.AddMinutes(3)
            .ToOffset(TimeSpan.FromHours(-3));

        Assert.True(appointment.ConfirmPayment(payment, now));

        Assert.Equal(
            CreatedAt.AddMinutes(3),
            appointment.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, appointment.UpdatedAt.Offset);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void ConfirmPayment_ShouldRespectExactReservationDeadline(
        int offsetTicks,
        bool expectedSuccess)
    {
        var appointment = CreateAppointment();
        var payment = CreateConfirmedPayment(appointment);

        var now = appointment.ReservationExpiresAt!.Value
            .AddTicks(offsetTicks);

        if (expectedSuccess)
        {
            Assert.True(appointment.ConfirmPayment(payment, now));
            Assert.Equal(
                AppointmentStatus.Confirmed,
                appointment.Status);
            Assert.Equal(now, appointment.UpdatedAt);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() =>
                appointment.ConfirmPayment(payment, now));

            AssertPendingAndUnchanged(appointment);
        }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("business")]
    [InlineData("appointment")]
    public void ConfirmPayment_WithForeignPayment_ShouldReject(
        string field)
    {
        var appointment = CreateAppointment();

        var foreignAppointment = CreateAppointment(
            tenantId: field == "tenant"
                ? Guid.NewGuid()
                : appointment.TenantId,
            businessId: field == "business"
                ? Guid.NewGuid()
                : appointment.BusinessId,
            appointmentId: field == "appointment"
                ? Guid.NewGuid()
                : appointment.Id);

        var payment = CreateConfirmedPayment(foreignAppointment);

        Assert.Throws<ArgumentException>(() =>
            appointment.ConfirmPayment(
                payment,
                CreatedAt.AddMinutes(3)));

        AssertPendingAndUnchanged(appointment);
    }

    [Theory]
    [InlineData("payment")]
    [InlineData("appointment")]
    public void ConfirmPayment_WithStaleClock_ShouldReject(
        string scenario)
    {
        var appointment = CreateAppointment();
        var payment = CreateConfirmedPayment(appointment);

        DateTimeOffset now;

        if (scenario == "appointment")
        {
            appointment.Reschedule(
                appointment.LocationId,
                appointment.ProfessionalId,
                appointment.StartsAt.AddHours(1),
                CreatedAt.AddMinutes(6));

            now = CreatedAt.AddMinutes(5);
        }
        else
        {
            now = CreatedAt.AddMinutes(1);
        }

        var previousUpdate = appointment.UpdatedAt;
        var previousStart = appointment.StartsAt;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            appointment.ConfirmPayment(payment, now));

        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Equal(previousUpdate, appointment.UpdatedAt);
        Assert.Equal(previousStart, appointment.StartsAt);
    }

    #endregion

    #region Helpers

    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static Appointment CreateAppointment(
        Guid? tenantId = null,
        Guid? businessId = null,
        Guid? appointmentId = null,
        decimal amount = 65m)
    {
        var tenant = tenantId ?? Guid.NewGuid();
        var id = appointmentId ?? Guid.NewGuid();

        var item = new AppointmentItem(
            Guid.NewGuid(),
            tenant,
            id,
            Guid.NewGuid(),
            "Serviço de teste",
            amount,
            45);

        return new Appointment(
            id,
            tenant,
            businessId ?? Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreatedAt.AddDays(1),
            CreatedAt,
            TimeSpan.FromMinutes(10),
            [item]);
    }

    private static Payment CreateConfirmedPayment(
        Appointment appointment)
    {
        var payment = new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt.AddMinutes(1));

        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.CreditCard,
            CreatedAt.AddMinutes(1),
            appointment.ReservationExpiresAt!.Value);

        attempt.RegisterProviderPayment(
            "MERCADO_PAGO",
            $"ORDTST{Guid.NewGuid():N}",
            CreatedAt.AddMinutes(1));

        attempt.Confirm(
            payment.Amount,
            CreatedAt.AddMinutes(2));

        payment.Confirm(
            attempt,
            CreatedAt.AddMinutes(2));

        return payment;
    }

    private static void AssertPendingAndUnchanged(
        Appointment appointment)
    {
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);
    }

    #endregion
}