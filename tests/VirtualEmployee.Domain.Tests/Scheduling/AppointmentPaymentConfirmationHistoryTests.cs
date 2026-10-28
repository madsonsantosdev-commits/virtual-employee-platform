using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AppointmentPaymentConfirmationHistoryTests
{
    #region Facts

    [Fact]
    public void PaymentConfirmed_ShouldCaptureTransitionInUtc()
    {
        var before = CreateState();
        var after = before with
        {
            Status = AppointmentStatus.Confirmed
        };

        var occurredAt = CreatedAt.AddMinutes(3)
            .ToOffset(TimeSpan.FromHours(-3));

        var entry = CreateEntry(
            before,
            after,
            occurredAt);

        Assert.Equal(
            AppointmentHistoryType.PaymentConfirmed,
            entry.Type);
        Assert.Equal(before, entry.Before);
        Assert.Equal(after, entry.After);
        Assert.Equal(
            CreatedAt.AddMinutes(3),
            entry.OccurredAt);
        Assert.Equal(TimeSpan.Zero, entry.OccurredAt.Offset);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void PaymentConfirmed_WithoutBeforeState_ShouldReject()
    {
        var after = CreateState() with
        {
            Status = AppointmentStatus.Confirmed
        };

        Assert.Throws<ArgumentException>(() =>
            CreateEntry(null, after));
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void PaymentConfirmed_ShouldRespectExactDeadline(
        int offsetTicks,
        bool expectedSuccess)
    {
        var before = CreateState();
        var after = before with
        {
            Status = AppointmentStatus.Confirmed
        };

        var occurredAt = before.ReservationExpiresAt!.Value
            .AddTicks(offsetTicks);

        if (expectedSuccess)
        {
            var entry = CreateEntry(
                before,
                after,
                occurredAt);

            Assert.Equal(
                AppointmentHistoryType.PaymentConfirmed,
                entry.Type);
            Assert.Equal(occurredAt, entry.OccurredAt);
        }
        else
        {
            Assert.Throws<ArgumentException>(() =>
                CreateEntry(
                    before,
                    after,
                    occurredAt));
        }
    }

    [Theory]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("interval")]
    [InlineData("duration")]
    [InlineData("expiration")]
    public void PaymentConfirmed_WithOtherStateChanges_ShouldReject(
        string field)
    {
        var before = CreateState();

        var confirmed = before with
        {
            Status = AppointmentStatus.Confirmed
        };

        var after = field switch
        {
            "location" => confirmed with
            {
                LocationId = Guid.NewGuid()
            },

            "professional" => confirmed with
            {
                ProfessionalId = Guid.NewGuid()
            },

            "interval" => confirmed with
            {
                StartsAt = confirmed.StartsAt.AddHours(1),
                EndsAt = confirmed.EndsAt.AddHours(1)
            },

            "duration" => confirmed with
            {
                EndsAt = confirmed.EndsAt.AddMinutes(1)
            },

            "expiration" => confirmed with
            {
                ReservationExpiresAt =
                    confirmed.ReservationExpiresAt!.Value.AddMinutes(1)
            },

            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };

        Assert.Throws<ArgumentException>(() =>
            CreateEntry(before, after));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Motivo não permitido nesta operação")]
    public void PaymentConfirmed_WithReason_ShouldReject(
        string reason)
    {
        var before = CreateState();
        var after = before with
        {
            Status = AppointmentStatus.Confirmed
        };

        Assert.Throws<ArgumentException>(() =>
            CreateEntry(
                before,
                after,
                reason: reason));
    }

    [Theory]
    [InlineData(
        AppointmentStatus.Expired,
        AppointmentStatus.Confirmed)]
    [InlineData(
        AppointmentStatus.CancelledByClient,
        AppointmentStatus.Confirmed)]
    [InlineData(
        AppointmentStatus.Pending,
        AppointmentStatus.Expired)]
    [InlineData(
        AppointmentStatus.Pending,
        AppointmentStatus.Pending)]
    public void PaymentConfirmed_WithInvalidTransition_ShouldReject(
        AppointmentStatus previousStatus,
        AppointmentStatus nextStatus)
    {
        var before = CreateState() with
        {
            Status = previousStatus
        };

        var after = before with
        {
            Status = nextStatus
        };

        Assert.Throws<ArgumentException>(() =>
            CreateEntry(before, after));
    }

    #endregion

    #region Helpers

    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static AppointmentHistoryState CreateState()
    {
        return new AppointmentHistoryState(
            AppointmentStatus.Pending,
            Guid.NewGuid(),
            Guid.NewGuid(),
            CreatedAt.AddDays(1),
            CreatedAt.AddDays(1).AddMinutes(45),
            CreatedAt.AddMinutes(10));
    }

    private static AppointmentHistoryEntry CreateEntry(
        AppointmentHistoryState? before,
        AppointmentHistoryState after,
        DateTimeOffset? occurredAt = null,
        string? reason = null)
    {
        return new AppointmentHistoryEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            AppointmentHistoryType.PaymentConfirmed,
            occurredAt ?? CreatedAt.AddMinutes(3),
            before,
            after,
            reason);
    }

    #endregion
}