using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Payments;

public sealed class PaymentAttemptTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Constructor_ShouldCopyPaymentIdentityAndAmount()
    {
        var payment = CreatePayment();
        var attemptId = Guid.NewGuid();
        var now = CreatedAt.AddMinutes(1);
        var expiresAt = CreatedAt.AddMinutes(10);

        var attempt = new PaymentAttempt(
            attemptId,
            payment,
            PaymentMethod.Pix,
            now,
            expiresAt);

        Assert.Equal(attemptId, attempt.Id);
        Assert.Equal(payment.TenantId, attempt.TenantId);
        Assert.Equal(payment.Id, attempt.PaymentId);
        Assert.Equal(payment.Amount, attempt.Amount);
        Assert.Equal(PaymentMethod.Pix, attempt.PaymentMethod);
        Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
        Assert.Equal(now, attempt.CreatedAt);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal(expiresAt, attempt.ExpiresAt);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(CreatedAt, payment.UpdatedAt);
    }

    [Fact]
    public void Constructor_ShouldNormalizeTimestampsToUtc()
    {
        var payment = CreatePayment();

        var localCreatedAt = new DateTimeOffset(
            2026, 10, 23, 9, 1, 0,
            TimeSpan.FromHours(-3));

        var localExpiresAt = localCreatedAt.AddMinutes(5);

        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.Pix,
            localCreatedAt,
            localExpiresAt);

        Assert.Equal(CreatedAt.AddMinutes(1), attempt.CreatedAt);
        Assert.Equal(CreatedAt.AddMinutes(6), attempt.ExpiresAt);
        Assert.Equal(attempt.CreatedAt, attempt.UpdatedAt);

        Assert.Equal(TimeSpan.Zero, attempt.CreatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, attempt.UpdatedAt.Offset);
        Assert.Equal(TimeSpan.Zero, attempt.ExpiresAt.Offset);
    }

    [Fact]
    public void Constructor_WithEmptyId_ShouldThrow()
    {
        var payment = CreatePayment();

        var exception = Assert.Throws<ArgumentException>(() =>
            new PaymentAttempt(
                Guid.Empty,
                payment,
                PaymentMethod.Pix,
                CreatedAt,
                CreatedAt.AddMinutes(5)));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullPayment_ShouldThrow()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new PaymentAttempt(
                Guid.NewGuid(),
                null!,
                PaymentMethod.Pix,
                CreatedAt,
                CreatedAt.AddMinutes(5)));

        Assert.Equal("payment", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithCreationBeforePayment_ShouldThrow()
    {
        var payment = CreatePayment();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaymentAttempt(
                Guid.NewGuid(),
                payment,
                PaymentMethod.Pix,
                CreatedAt.AddTicks(-1),
                CreatedAt.AddMinutes(5)));

        Assert.Equal("createdAt", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithDifferentAttempts_ShouldPreservePayment()
    {
        var payment = CreatePayment();

        var first = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.Pix,
            CreatedAt,
            CreatedAt.AddMinutes(5));

        var second = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.CreditCard,
            CreatedAt.AddMinutes(1),
            CreatedAt.AddMinutes(6));

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(payment.Id, first.PaymentId);
        Assert.Equal(payment.Id, second.PaymentId);
        Assert.Equal(payment.Amount, first.Amount);
        Assert.Equal(payment.Amount, second.Amount);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(CreatedAt, payment.UpdatedAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(PaymentMethod.Pix)]
    [InlineData(PaymentMethod.CreditCard)]
    [InlineData(PaymentMethod.DebitCard)]
    public void Constructor_WithSupportedMethod_ShouldSucceed(
        PaymentMethod paymentMethod)
    {
        var payment = CreatePayment();

        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            paymentMethod,
            CreatedAt,
            CreatedAt.AddMinutes(5));

        Assert.Equal(paymentMethod, attempt.PaymentMethod);
        Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(999)]
    public void Constructor_WithInvalidMethod_ShouldThrow(
        int paymentMethod)
    {
        var payment = CreatePayment();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaymentAttempt(
                Guid.NewGuid(),
                payment,
                (PaymentMethod)paymentMethod,
                CreatedAt,
                CreatedAt.AddMinutes(5)));

        Assert.Equal("paymentMethod", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Constructor_WithExpirationNotAfterCreation_ShouldThrow(
        int expirationOffsetTicks)
    {
        var payment = CreatePayment();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PaymentAttempt(
                Guid.NewGuid(),
                payment,
                PaymentMethod.Pix,
                CreatedAt,
                CreatedAt.AddTicks(expirationOffsetTicks)));

        Assert.Equal("expiresAt", exception.ParamName);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("65.50")]
    [InlineData("9999999999.99")]
    public void Constructor_ShouldPreserveExactPaymentAmount(
        string amountText)
    {
        var amount = decimal.Parse(
            amountText,
            System.Globalization.CultureInfo.InvariantCulture);

        var payment = CreatePayment(amount);

        var attempt = new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.Pix,
            CreatedAt,
            CreatedAt.AddMinutes(5));

        Assert.Equal(amount, attempt.Amount);
    }

    #endregion

    #region Helpers

    private static Payment CreatePayment(decimal price = 65m)
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

        var appointment = new Appointment(
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

        return new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt);
    }

    #endregion
}