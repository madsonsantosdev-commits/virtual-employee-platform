using VirtualEmployee.Domain.Payments;
using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Payments;

public sealed class PaymentAttemptProviderTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Cancel_FromCreated_ShouldCancelWithoutProvider()
    {
        var attempt = CreateAttempt();
        var now = CreatedAt.AddMinutes(1);

        Assert.True(attempt.Cancel(now));

        Assert.Equal(PaymentAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Null(attempt.Provider);
        Assert.Null(attempt.ProviderPaymentId);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Fact]
    public void Cancel_FromPending_ShouldPreserveProviderAndFinancialData()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var now = CreatedAt.AddMinutes(1);

        Assert.True(attempt.Cancel(now));

        Assert.Equal(PaymentAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(65m, attempt.Amount);
        Assert.Equal(CreatedAt, attempt.CreatedAt);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Fact]
    public void Cancel_FromProcessing_ShouldNormalizeUtcAndPreserveReplayAudit()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        attempt.MarkProcessing(CreatedAt.AddMinutes(1));

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 2, 0,
            TimeSpan.FromHours(-3));

        Assert.True(attempt.Cancel(localNow));
        Assert.False(attempt.Cancel(localNow.AddMinutes(1)));
        Assert.False(attempt.Cancel(CreatedAt));

        Assert.Equal(PaymentAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(CreatedAt.AddMinutes(2), attempt.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, attempt.UpdatedAt.Offset);
    }

    [Fact]
    public void Cancel_WithClockBeforeLastUpdate_ShouldThrow()
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.Cancel(registeredAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Cancel_AfterConfirmation_ShouldPreserveFinancialTruth()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var confirmedAt = CreatedAt.AddMinutes(1);
        attempt.Confirm(65m, confirmedAt);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Cancel(confirmedAt.AddMinutes(1)));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Cancel_AfterFailure_ShouldPreserveFailure()
    {
        var attempt = CreateAttempt();
        var failedAt = CreatedAt.AddMinutes(1);

        attempt.Fail(failedAt);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Cancel(failedAt.AddMinutes(1)));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(failedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Cancel_AfterExpiration_ShouldPreserveExpiration()
    {
        var attempt = CreateAttempt();
        var expiredAt = attempt.ExpiresAt;

        attempt.Expire(expiredAt);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Cancel(expiredAt.AddMinutes(1)));

        Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);
        Assert.Equal(expiredAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Confirm_AfterCancellation_ShouldRecordFinancialTruth()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        attempt.Cancel(CreatedAt.AddMinutes(1));

        var confirmedAt = CreatedAt.AddMinutes(6);

        Assert.True(attempt.Confirm(65m, confirmedAt));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
    }

    [Fact]
    public void RegisterProviderPayment_ReplayAfterCancellation_ShouldNotReactivate()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var cancelledAt = CreatedAt.AddMinutes(1);
        attempt.Cancel(cancelledAt);

        Assert.False(attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            cancelledAt.AddMinutes(1)));

        Assert.Equal(PaymentAttemptStatus.Cancelled, attempt.Status);
        Assert.Equal(cancelledAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Expire_FromCreated_ShouldPreserveFinancialData()
    {
        var attempt = CreateAttempt();
        var now = attempt.ExpiresAt;

        Assert.True(attempt.Expire(now));

        Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal(65m, attempt.Amount);
        Assert.Equal(CreatedAt, attempt.CreatedAt);
        Assert.Null(attempt.Provider);
        Assert.Null(attempt.ProviderPaymentId);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Fact]
    public void Expire_FromProcessing_ShouldNormalizeUtcAndPreserveReplayAudit()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        attempt.MarkProcessing(CreatedAt.AddMinutes(1));

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 5, 0,
            TimeSpan.FromHours(-3));

        Assert.True(attempt.Expire(localNow));
        Assert.False(attempt.Expire(localNow.AddMinutes(1)));
        Assert.False(attempt.Expire(CreatedAt));

        Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, attempt.UpdatedAt.Offset);
        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
    }

    [Fact]
    public void Expire_WithClockBeforeLastUpdate_ShouldThrow()
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.Expire(registeredAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Expire_AfterConfirmation_ShouldPreserveFinancialTruth()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var confirmedAt = CreatedAt.AddMinutes(1);
        attempt.Confirm(65m, confirmedAt);

        Assert.False(attempt.Expire(attempt.ExpiresAt));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Expire_AfterFailure_ShouldPreserveFailureAudit()
    {
        var attempt = CreateAttempt();
        var failedAt = CreatedAt.AddMinutes(1);

        attempt.Fail(failedAt);

        Assert.False(attempt.Expire(attempt.ExpiresAt));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(failedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Confirm_AfterExpiration_ShouldRecordFinancialTruth()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        Assert.True(attempt.Expire(attempt.ExpiresAt));

        var confirmedAt = CreatedAt.AddMinutes(6);

        Assert.True(attempt.Confirm(65m, confirmedAt));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
    }

    [Fact]
    public void RegisterProviderPayment_ReplayAfterExpiration_ShouldNotReactivate()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var expiredAt = attempt.ExpiresAt;
        attempt.Expire(expiredAt);

        Assert.False(attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            expiredAt.AddMinutes(1)));

        Assert.Equal(PaymentAttemptStatus.Expired, attempt.Status);
        Assert.Equal(expiredAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Fail_FromCreated_ShouldRecordFailureWithoutProvider()
    {
        var attempt = CreateAttempt();
        var now = CreatedAt.AddMinutes(1);

        Assert.True(attempt.Fail(now));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Null(attempt.Provider);
        Assert.Null(attempt.ProviderPaymentId);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Fact]
    public void Fail_FromPending_ShouldPreserveProviderAndFinancialData()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var now = CreatedAt.AddMinutes(1);

        Assert.True(attempt.Fail(now));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(65m, attempt.Amount);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Fact]
    public void Fail_FromProcessing_ShouldNormalizeUtcAndPreserveReplayAudit()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        attempt.MarkProcessing(CreatedAt.AddMinutes(1));

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 2, 0,
            TimeSpan.FromHours(-3));

        Assert.True(attempt.Fail(localNow));
        Assert.False(attempt.Fail(localNow.AddMinutes(1)));
        Assert.False(attempt.Fail(CreatedAt));

        Assert.Equal(PaymentAttemptStatus.Failed, attempt.Status);
        Assert.Equal(CreatedAt.AddMinutes(2), attempt.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, attempt.UpdatedAt.Offset);
    }

    [Fact]
    public void Fail_WithClockBeforeLastUpdate_ShouldThrow()
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.Fail(registeredAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Fail_AfterConfirmation_ShouldPreserveFinancialTruth()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var confirmedAt = CreatedAt.AddMinutes(1);
        attempt.Confirm(65m, confirmedAt);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Fail(confirmedAt.AddMinutes(1)));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Confirm_AfterFailure_ShouldRecordLateFinancialConfirmation()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        attempt.Fail(CreatedAt.AddMinutes(1));

        var confirmedAt = CreatedAt.AddMinutes(6);

        Assert.True(attempt.Confirm(65m, confirmedAt));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
    }

    [Fact]
    public void Confirm_FromPending_ShouldRecordFinancialConfirmation()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var now = CreatedAt.AddMinutes(1);

        Assert.True(attempt.Confirm(65m, now));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(now, attempt.ConfirmedAt);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal(65m, attempt.Amount);
        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
    }

    [Fact]
    public void Confirm_FromProcessing_ShouldNormalizeUtc()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        attempt.MarkProcessing(CreatedAt.AddMinutes(1));

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 2, 0,
            TimeSpan.FromHours(-3));

        Assert.True(attempt.Confirm(65m, localNow));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(CreatedAt.AddMinutes(2), attempt.ConfirmedAt);
        Assert.Equal(TimeSpan.Zero, attempt.ConfirmedAt!.Value.Offset);
        Assert.Equal(CreatedAt.AddMinutes(2), attempt.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, attempt.UpdatedAt.Offset);
    }

    [Fact]
    public void Confirm_WithRepeatedNotification_ShouldPreserveAudit()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var confirmedAt = CreatedAt.AddMinutes(1);

        Assert.True(attempt.Confirm(65m, confirmedAt));
        Assert.False(attempt.Confirm(65m, confirmedAt.AddMinutes(1)));
        Assert.False(attempt.Confirm(65m, CreatedAt));

        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void Confirm_WithoutProviderAssociation_ShouldThrow()
    {
        var attempt = CreateAttempt();

        Assert.Throws<InvalidOperationException>(() =>
            attempt.Confirm(65m, CreatedAt));

        AssertUnregistered(attempt);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Fact]
    public void Confirm_WithClockBeforeLastUpdate_ShouldThrow()
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.Confirm(65m, registeredAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Fact]
    public void Confirm_AfterLocalDeadline_ShouldRecordFinancialTruth()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var now = CreatedAt.AddMinutes(6);

        Assert.True(attempt.Confirm(65m, now));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(now, attempt.ConfirmedAt);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
    }

    [Fact]
    public void MarkProcessing_AfterConfirmation_ShouldNotRegressStatus()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var confirmedAt = CreatedAt.AddMinutes(1);
        attempt.Confirm(65m, confirmedAt);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.MarkProcessing(confirmedAt.AddMinutes(1)));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
    }

    [Fact]
    public void MarkProcessing_ShouldPreserveProviderAndFinancialData()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt.AddMinutes(1));

        var now = CreatedAt.AddMinutes(2);

        Assert.True(attempt.MarkProcessing(now));

        Assert.Equal(PaymentAttemptStatus.Processing, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(65m, attempt.Amount);
        Assert.Equal(CreatedAt, attempt.CreatedAt);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
    }

    [Fact]
    public void MarkProcessing_WithRepeatedNotification_ShouldPreserveAudit()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var processedAt = CreatedAt.AddMinutes(1);

        Assert.True(attempt.MarkProcessing(processedAt));

        Assert.False(attempt.MarkProcessing(processedAt.AddMinutes(1)));
        Assert.False(attempt.MarkProcessing(CreatedAt));

        Assert.Equal(processedAt, attempt.UpdatedAt);
        Assert.Equal(PaymentAttemptStatus.Processing, attempt.Status);

        // Repetir a associação também não regride para Pending.
        Assert.False(attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            processedAt.AddMinutes(2)));

        Assert.Equal(processedAt, attempt.UpdatedAt);
        Assert.Equal(PaymentAttemptStatus.Processing, attempt.Status);
    }

    [Fact]
    public void MarkProcessing_WithoutProviderAssociation_ShouldThrow()
    {
        var attempt = CreateAttempt();

        Assert.Throws<InvalidOperationException>(() =>
            attempt.MarkProcessing(CreatedAt.AddMinutes(1)));

        AssertUnregistered(attempt);
    }

    [Fact]
    public void MarkProcessing_WithClockBeforeLastUpdate_ShouldThrow()
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.MarkProcessing(registeredAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
    }

    [Fact]
    public void MarkProcessing_WithLateNotification_ShouldNormalizeUtc()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 6, 0,
            TimeSpan.FromHours(-3));

        Assert.True(attempt.MarkProcessing(localNow));

        Assert.Equal(PaymentAttemptStatus.Processing, attempt.Status);
        Assert.Equal(CreatedAt.AddMinutes(6), attempt.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, attempt.UpdatedAt.Offset);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
    }

    [Fact]
    public void RegisterProviderPayment_ShouldAssociateAndMarkPending()
    {
        var attempt = CreateAttempt();
        var now = CreatedAt.AddMinutes(1);

        var changed = attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            now);

        Assert.True(changed);
        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal(CreatedAt, attempt.CreatedAt);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
        Assert.Equal(65m, attempt.Amount);
    }

    [Fact]
    public void RegisterProviderPayment_ShouldTrimReferencesAndPreserveCase()
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "  Gateway-Test  ",
            "  Payment-AbC123  ",
            CreatedAt);

        Assert.Equal("Gateway-Test", attempt.Provider);
        Assert.Equal("Payment-AbC123", attempt.ProviderPaymentId);
    }

    [Fact]
    public void RegisterProviderPayment_ShouldNormalizeTimestampToUtc()
    {
        var attempt = CreateAttempt();

        var localNow = new DateTimeOffset(
            2026, 10, 23, 9, 1, 0,
            TimeSpan.FromHours(-3));

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            localNow);

        Assert.Equal(CreatedAt.AddMinutes(1), attempt.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, attempt.UpdatedAt.Offset);
    }

    [Fact]
    public void RegisterProviderPayment_WithSameReferences_ShouldPreserveAudit()
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        Assert.True(attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt));

        var changed = attempt.RegisterProviderPayment(
            "  gateway-test  ",
            "  payment-123  ",
            registeredAt.AddMinutes(1));

        Assert.False(changed);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
    }

    [Fact]
    public void RegisterProviderPayment_WithEarlierReplayClock_ShouldBeNoOp()
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt);

        var changed = attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        Assert.False(changed);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
    }

    [Fact]
    public void RegisterProviderPayment_WithClockBeforeCreation_ShouldThrow()
    {
        var attempt = CreateAttempt();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.RegisterProviderPayment(
                "gateway-test",
                "payment-123",
                CreatedAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        AssertUnregistered(attempt);
    }

    [Fact]
    public void RegisterProviderPayment_WithMaximumReferenceLengths_ShouldSucceed()
    {
        var attempt = CreateAttempt();
        var provider = new string(
            'p',
            PaymentAttempt.ProviderMaxLength);
        var paymentId = new string(
            'x',
            PaymentAttempt.ProviderPaymentIdMaxLength);

        Assert.True(attempt.RegisterProviderPayment(
            provider,
            paymentId,
            CreatedAt));

        Assert.Equal(provider, attempt.Provider);
        Assert.Equal(paymentId, attempt.ProviderPaymentId);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(PaymentAttemptStatus.Failed)]
    [InlineData(PaymentAttemptStatus.Expired)]
    [InlineData(PaymentAttemptStatus.Cancelled)]
    public void RegisterProviderPayment_WithLateInitialResponse_ShouldPreserveState(
        PaymentAttemptStatus terminalStatus)
    {
        var attempt = CreateAttempt();

        switch (terminalStatus)
        {
            case PaymentAttemptStatus.Failed:
                attempt.Fail(CreatedAt.AddMinutes(1));
                break;

            case PaymentAttemptStatus.Expired:
                attempt.Expire(attempt.ExpiresAt);
                break;

            case PaymentAttemptStatus.Cancelled:
                attempt.Cancel(CreatedAt.AddMinutes(1));
                break;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(terminalStatus));
        }

        var registeredAt = CreatedAt.AddMinutes(6);

        // O registro tardio também deve respeitar a ordem das atualizações.
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.RegisterProviderPayment(
                "gateway-test",
                "payment-123",
                attempt.UpdatedAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Null(attempt.Provider);
        Assert.Null(attempt.ProviderPaymentId);
        Assert.Equal(terminalStatus, attempt.Status);

        Assert.True(attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt));

        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(terminalStatus, attempt.Status);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
        Assert.Null(attempt.ConfirmedAt);

        Assert.False(attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt.AddMinutes(1)));

        Assert.Equal(terminalStatus, attempt.Status);
        Assert.Equal(registeredAt, attempt.UpdatedAt);

        // Uma confirmação financeira posterior continua sendo registrada.
        var confirmedAt = registeredAt.AddMinutes(2);

        Assert.True(attempt.Confirm(65m, confirmedAt));

        Assert.Equal(PaymentAttemptStatus.Confirmed, attempt.Status);
        Assert.Equal(confirmedAt, attempt.ConfirmedAt);
        Assert.Equal(confirmedAt, attempt.UpdatedAt);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Expire_FromPending_ShouldRespectExactDeadline(
        int offsetTicks,
        bool expectedChange)
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var now = attempt.ExpiresAt.AddTicks(offsetTicks);

        Assert.Equal(expectedChange, attempt.Expire(now));

        Assert.Equal(
            expectedChange
                ? PaymentAttemptStatus.Expired
                : PaymentAttemptStatus.Pending,
            attempt.Status);

        Assert.Equal(
            expectedChange ? now : CreatedAt,
            attempt.UpdatedAt);

        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Null(attempt.ConfirmedAt);
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("64.99", false)]
    [InlineData("65.01", false)]
    [InlineData("65.001", false)]
    [InlineData("64.99", true)]
    public void Confirm_WithDifferentAmount_ShouldThrow(
        string amountText,
        bool alreadyConfirmed)
    {
        var attempt = CreateAttempt();

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            CreatedAt);

        var previousUpdate = CreatedAt;

        if (alreadyConfirmed)
        {
            previousUpdate = CreatedAt.AddMinutes(1);
            attempt.Confirm(65m, previousUpdate);
        }

        var amount = decimal.Parse(
            amountText,
            System.Globalization.CultureInfo.InvariantCulture);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.Confirm(amount, CreatedAt.AddMinutes(2)));

        Assert.Equal("confirmedAmount", exception.ParamName);
        Assert.Equal(previousUpdate, attempt.UpdatedAt);
        Assert.Equal(
            alreadyConfirmed
                ? PaymentAttemptStatus.Confirmed
                : PaymentAttemptStatus.Pending,
            attempt.Status);

        if (alreadyConfirmed)
        {
            Assert.Equal(previousUpdate, attempt.ConfirmedAt);
        }
        else
        {
            Assert.Null(attempt.ConfirmedAt);
        }
    }

    [Theory]
    [InlineData(300)]
    [InlineData(301)]
    public void RegisterProviderPayment_WithLateResponse_ShouldPreserveReference(
        int elapsedSeconds)
    {
        var attempt = CreateAttempt();
        var now = CreatedAt.AddSeconds(elapsedSeconds);

        var changed = attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            now);

        Assert.True(changed);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
        Assert.Equal(now, attempt.UpdatedAt);
        Assert.Equal(CreatedAt.AddMinutes(5), attempt.ExpiresAt);
    }

    [Theory]
    [InlineData("other-gateway", "payment-123")]
    [InlineData("gateway-test", "other-payment")]
    [InlineData("other-gateway", "other-payment")]
    [InlineData("Gateway-Test", "payment-123")]
    [InlineData("gateway-test", "Payment-123")]
    public void RegisterProviderPayment_WithDifferentReferences_ShouldThrow(
        string provider,
        string providerPaymentId)
    {
        var attempt = CreateAttempt();
        var registeredAt = CreatedAt.AddMinutes(1);

        attempt.RegisterProviderPayment(
            "gateway-test",
            "payment-123",
            registeredAt);

        Assert.Throws<InvalidOperationException>(() =>
            attempt.RegisterProviderPayment(
                provider,
                providerPaymentId,
                registeredAt.AddMinutes(1)));

        Assert.Equal("gateway-test", attempt.Provider);
        Assert.Equal("payment-123", attempt.ProviderPaymentId);
        Assert.Equal(registeredAt, attempt.UpdatedAt);
        Assert.Equal(PaymentAttemptStatus.Pending, attempt.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gate way")]
    [InlineData("gate\tway")]
    [InlineData("gate\nway")]
    [InlineData("gate\u0000way")]
    public void RegisterProviderPayment_WithInvalidProvider_ShouldThrow(
        string? provider)
    {
        var attempt = CreateAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.RegisterProviderPayment(
                provider!,
                "payment-123",
                CreatedAt));

        Assert.Equal("provider", exception.ParamName);
        AssertUnregistered(attempt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("payment 123")]
    [InlineData("payment\t123")]
    [InlineData("payment\n123")]
    [InlineData("payment\u0000123")]
    public void RegisterProviderPayment_WithInvalidPaymentId_ShouldThrow(
        string? providerPaymentId)
    {
        var attempt = CreateAttempt();

        var exception = Assert.Throws<ArgumentException>(() =>
            attempt.RegisterProviderPayment(
                "gateway-test",
                providerPaymentId!,
                CreatedAt));

        Assert.Equal("providerPaymentId", exception.ParamName);
        AssertUnregistered(attempt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RegisterProviderPayment_WithOversizedReference_ShouldThrow(
        bool oversizedProvider)
    {
        var attempt = CreateAttempt();

        var provider = oversizedProvider
            ? new string('p', PaymentAttempt.ProviderMaxLength + 1)
            : "gateway-test";

        var paymentId = oversizedProvider
            ? "payment-123"
            : new string('x', PaymentAttempt.ProviderPaymentIdMaxLength + 1);

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            attempt.RegisterProviderPayment(
                provider,
                paymentId,
                CreatedAt));

        Assert.Equal(
            oversizedProvider ? "provider" : "providerPaymentId",
            exception.ParamName);

        AssertUnregistered(attempt);
    }

    #endregion

    #region Helpers

    private static void AssertUnregistered(PaymentAttempt attempt)
    {
        Assert.Null(attempt.Provider);
        Assert.Null(attempt.ProviderPaymentId);
        Assert.Equal(PaymentAttemptStatus.Created, attempt.Status);
        Assert.Equal(CreatedAt, attempt.UpdatedAt);
    }

    private static PaymentAttempt CreateAttempt()
    {
        var tenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();

        var item = new AppointmentItem(
            Guid.NewGuid(),
            tenantId,
            appointmentId,
            Guid.NewGuid(),
            "Corte",
            65m,
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

        var payment = new Payment(
            Guid.NewGuid(),
            appointment,
            CreatedAt);

        return new PaymentAttempt(
            Guid.NewGuid(),
            payment,
            PaymentMethod.Pix,
            CreatedAt,
            CreatedAt.AddMinutes(5));
    }

    #endregion
}