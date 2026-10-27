using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Confirm_WithConfirmedAttempt_ShouldSetSettlementReference()
    {
        var appointment = CreateAppointment();
        var payment = new Payment(Guid.NewGuid(), appointment, CreatedAt);
        var attempt = CreateConfirmedAttempt(payment);
        var now = CreatedAt.AddMinutes(2);

        Assert.True(payment.Confirm(attempt, now));

        Assert.Equal(PaymentStatus.Confirmed, payment.Status);
        Assert.Equal(attempt.Id, payment.ConfirmedPaymentAttemptId);
        Assert.Equal(now, payment.ConfirmedAt);
        Assert.Equal(now, payment.UpdatedAt);
        Assert.Equal(CreatedAt, payment.CreatedAt);
        Assert.Equal(appointment.TotalPriceSnapshot, payment.Amount);

        // A confirmação operacional será coordenada pela Application.
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
    }

    [Fact]
    public void Confirm_WithSameAttempt_ShouldPreserveOriginalAudit()
    {
        var payment = new Payment(
            Guid.NewGuid(),
            CreateAppointment(),
            CreatedAt);

        var attempt = CreateConfirmedAttempt(payment);
        var confirmedAt = CreatedAt.AddMinutes(2);

        Assert.True(payment.Confirm(attempt, confirmedAt));
        Assert.False(payment.Confirm(attempt, confirmedAt.AddMinutes(1)));
        Assert.False(payment.Confirm(attempt, CreatedAt));

        Assert.Equal(attempt.Id, payment.ConfirmedPaymentAttemptId);
        Assert.Equal(confirmedAt, payment.ConfirmedAt);
        Assert.Equal(confirmedAt, payment.UpdatedAt);
    }

    [Fact]
    public void Confirm_WithAnotherConfirmedAttempt_ShouldPreserveFirstSettlement()
    {
        var payment = new Payment(
            Guid.NewGuid(),
            CreateAppointment(),
            CreatedAt);

        var first = CreateConfirmedAttempt(payment);
        var second = CreateConfirmedAttempt(payment);
        var confirmedAt = CreatedAt.AddMinutes(2);

        payment.Confirm(first, confirmedAt);

        Assert.Throws<InvalidOperationException>(() =>
            payment.Confirm(second, confirmedAt.AddMinutes(1)));

        Assert.Equal(first.Id, payment.ConfirmedPaymentAttemptId);
        Assert.Equal(confirmedAt, payment.ConfirmedAt);
        Assert.Equal(confirmedAt, payment.UpdatedAt);
        Assert.Equal(PaymentAttemptStatus.Confirmed, second.Status);
    }

    [Fact]
    public void Confirm_WithAttemptFromAnotherPayment_ShouldThrow()
    {
        var appointment = CreateAppointment();

        var payment = new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt);

        var otherPayment = new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt);

        var attempt = CreateConfirmedAttempt(otherPayment);

        var exception = Assert.Throws<ArgumentException>(() =>
            payment.Confirm(attempt, CreatedAt.AddMinutes(2)));

        Assert.Equal("attempt", exception.ParamName);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ConfirmedPaymentAttemptId);
        Assert.Null(payment.ConfirmedAt);
    }

    [Fact]
    public void Confirm_WithAttemptFromAnotherTenant_ShouldThrow()
    {
        var payment = new Payment(
            Guid.NewGuid(),
            CreateAppointment(),
            CreatedAt);

        // Mesmo ID lógico, mas pertencente a outro tenant.
        var otherPayment = new Payment(
            payment.Id,
            CreateAppointment(),
            CreatedAt);

        var attempt = CreateConfirmedAttempt(otherPayment);

        Assert.Throws<ArgumentException>(() =>
            payment.Confirm(attempt, CreatedAt.AddMinutes(2)));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ConfirmedPaymentAttemptId);
    }

    [Fact]
    public void Confirm_WithNullAttempt_ShouldThrow()
    {
        var payment = new Payment(
            Guid.NewGuid(),
            CreateAppointment(),
            CreatedAt);

        var exception = Assert.Throws<ArgumentNullException>(() =>
            payment.Confirm(null!, CreatedAt));

        Assert.Equal("attempt", exception.ParamName);
    }

    [Fact]
    public void Confirm_WithUnconfirmedAttempt_ShouldThrow()
    {
        var payment = new Payment(
            Guid.NewGuid(),
            CreateAppointment(),
            CreatedAt);

        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.Pix,
            CreatedAt,
            CreatedAt.AddMinutes(5));

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        Assert.Throws<InvalidOperationException>(() =>
            payment.Confirm(attempt, CreatedAt.AddMinutes(1)));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ConfirmedAt);
    }

    [Fact]
    public void Confirm_WithClockBeforeAttemptUpdate_ShouldThrow()
    {
        var payment = new Payment(
            Guid.NewGuid(),
            CreateAppointment(),
            CreatedAt);

        var attempt = CreateConfirmedAttempt(payment);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            payment.Confirm(
                attempt,
                attempt.UpdatedAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(CreatedAt, payment.UpdatedAt);
        Assert.Null(payment.ConfirmedAt);
    }

    [Fact]
    public void Confirm_ShouldNormalizeTimestampToUtc()
    {
        var payment = new Payment(
            Guid.NewGuid(),
            CreateAppointment(),
            CreatedAt);

        var attempt = CreateConfirmedAttempt(payment);

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 2, 0,
            TimeSpan.FromHours(-3));

        payment.Confirm(attempt, localNow);

        Assert.Equal(CreatedAt.AddMinutes(2), payment.ConfirmedAt);
        Assert.Equal(TimeSpan.Zero, payment.ConfirmedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, payment.UpdatedAt.Offset);
    }

    [Fact]
    public void Constructor_ShouldCopyAppointmentIdentityAndAmount()
    {
        var appointment = CreateAppointment(65.50m);
        var paymentId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(1);

        var payment = new Payment(
            paymentId,
            appointment,
            now);

        Assert.Equal(paymentId, payment.Id);
        Assert.Equal(appointment.TenantId, payment.TenantId);
        Assert.Equal(appointment.BusinessId, payment.BusinessId);
        Assert.Equal(appointment.Id, payment.AppointmentId);
        Assert.Equal(appointment.TotalPriceSnapshot, payment.Amount);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(now, payment.CreatedAt);
        Assert.Equal(now, payment.UpdatedAt);

        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);
    }

    [Fact]
    public void Constructor_ShouldNormalizeTimestampsToUtc()
    {
        var appointment = CreateAppointment();

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 1, 0,
            TimeSpan.FromHours(-3));

        var payment = new Payment(
            Guid.NewGuid(),
            appointment,
            localNow);

        Assert.Equal(CreatedAt.AddMinutes(1), payment.CreatedAt);
        Assert.Equal(TimeSpan.Zero, payment.CreatedAt.Offset);
        Assert.Equal(payment.CreatedAt, payment.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, payment.UpdatedAt.Offset);
    }

    [Fact]
    public void Constructor_WithEmptyId_ShouldThrow()
    {
        var appointment = CreateAppointment();

        var exception = Assert.Throws<ArgumentException>(() =>
            new Payment(
                Guid.Empty,
                appointment,
                CreatedAt));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullAppointment_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new Payment(
                Guid.NewGuid(),
                null!,
                CreatedAt));

        Assert.Equal("appointment", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithTimeBeforeLastUpdate_ShouldThrow()
    {
        var appointment = CreateAppointment();
        var updatedAt = CreatedAt.AddMinutes(2);

        appointment.Reschedule(
            appointment.LocationId,
            appointment.ProfessionalId,
            appointment.StartsAt.AddHours(1),
            updatedAt);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Payment(
                Guid.NewGuid(),
                appointment,
                updatedAt.AddTicks(-1)));

        Assert.Equal("createdAt", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithExpiredAppointment_ShouldThrow()
    {
        var appointment = CreateAppointment();
        var now = CreatedAt.AddMinutes(10);

        Assert.True(appointment.Expire(now));

        Assert.Throws<InvalidOperationException>(() =>
            new Payment(
                Guid.NewGuid(),
                appointment,
                now));
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(0)]
    [InlineData(599)]
    public void Constructor_BeforeReservationDeadline_ShouldSucceed(
        int elapsedSeconds)
    {
        var appointment = CreateAppointment();

        var payment = new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt.AddSeconds(elapsedSeconds));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Theory]
    [InlineData(600)]
    [InlineData(601)]
    public void Constructor_AtOrAfterReservationDeadline_ShouldThrow(
        int elapsedSeconds)
    {
        var appointment = CreateAppointment();

        Assert.Throws<InvalidOperationException>(() =>
            new Payment(
                Guid.NewGuid(),
                appointment,
                CreatedAt.AddSeconds(elapsedSeconds)));

        // Criar um Payment não materializa a expiração do Appointment.
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);
    }

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledByBusiness)]
    public void Constructor_WithCancelledAppointment_ShouldThrow(
        AppointmentStatus cancellationStatus)
    {
        var appointment = CreateAppointment();
        var now = CreatedAt.AddMinutes(1);

        appointment.Cancel(
            cancellationStatus,
            "Cancelamento de teste",
            now);

        Assert.Throws<InvalidOperationException>(() =>
            new Payment(
                Guid.NewGuid(),
                appointment,
                now));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.001")]
    [InlineData("65.501")]
    [InlineData("10000000000")]
    public void Constructor_WithUnsupportedAmount_ShouldThrow(
        string amountText)
    {
        var amount = decimal.Parse(
            amountText,
            System.Globalization.CultureInfo.InvariantCulture);

        var appointment = CreateAppointment(amount);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Payment(
                Guid.NewGuid(),
                appointment,
                CreatedAt));

        Assert.Equal("appointment", exception.ParamName);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("65.50")]
    [InlineData("9999999999.99")]
    public void Constructor_WithSupportedAmount_ShouldPreserveExactValue(
        string amountText)
    {
        var amount = decimal.Parse(
            amountText,
            System.Globalization.CultureInfo.InvariantCulture);

        var appointment = CreateAppointment(amount);

        var payment = new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt);

        Assert.Equal(amount, payment.Amount);
    }

    #endregion

    #region Helpers

    private static PaymentAttempt CreateConfirmedAttempt(Payment payment)
    {
        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.Pix,
            CreatedAt,
            CreatedAt.AddMinutes(5));

        attempt.RegisterProviderPayment(
            "gateway-test",
            Guid.NewGuid().ToString("N"),
            CreatedAt);

        attempt.Confirm( payment.Amount, CreatedAt.AddMinutes(1));

        return attempt;
    }

    private static Appointment CreateAppointment(decimal price = 65m)
    {
        var tenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        var item = new AppointmentItem(
            Guid.NewGuid(),
            tenantId,
            appointmentId,
            Guid.NewGuid(),
            "Corte",
            price,
            45);

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
            new[] { item });
    }

    #endregion
}