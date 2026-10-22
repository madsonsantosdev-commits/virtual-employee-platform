using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AppointmentHistoryEntryTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Created_ShouldCaptureInitialStateAndNormalizeOccurredAt()
    {
        var state = CreateState();
        var tenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var id = Guid.NewGuid();

        var entry = new AppointmentHistoryEntry(
            id,
            tenantId,
            appointmentId,
            AppointmentHistoryType.Created,
            CreatedAt.ToOffset(TimeSpan.FromHours(-3)),
            null,
            state);

        Assert.Equal(id, entry.Id);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(appointmentId, entry.AppointmentId);
        Assert.Equal(AppointmentHistoryType.Created, entry.Type);
        Assert.Equal(CreatedAt, entry.OccurredAt);
        Assert.Equal(TimeSpan.Zero, entry.OccurredAt.Offset);
        Assert.Null(entry.Before);
        Assert.Equal(state, entry.After);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void Rescheduled_ShouldPreservePreviousState()
    {
        var before = CreateState();

        var after = before with
        {
            LocationId = Guid.NewGuid(),
            ProfessionalId = Guid.NewGuid(),
            StartsAt = before.StartsAt.AddHours(1),
            EndsAt = before.EndsAt.AddHours(1)
        };

        var entry = CreateEntry(
            AppointmentHistoryType.Rescheduled,
            before,
            after);

        Assert.NotNull(entry.Before);

        Assert.Equal(before, entry.Before);
        Assert.Equal(after, entry.After);
        Assert.NotEqual(entry.Before.LocationId, entry.After.LocationId);
        Assert.NotEqual(
            entry.Before.ProfessionalId,
            entry.After.ProfessionalId);
        Assert.Equal(CreatedAt.AddHours(2), entry.Before.StartsAt);
        Assert.Equal(CreatedAt.AddHours(3), entry.After.StartsAt);
        Assert.Equal(
            entry.Before.ReservationExpiresAt,
            entry.After.ReservationExpiresAt);
    }

    [Fact]
    public void Expired_AtDeadline_ShouldRecordTransition()
    {
        var before = CreateState();
        var after = before with { Status = AppointmentStatus.Expired };

        var entry = CreateEntry(
            AppointmentHistoryType.Expired,
            before,
            after,
            occurredAt: before.ReservationExpiresAt);

        Assert.Equal(AppointmentStatus.Pending, entry.Before!.Status);
        Assert.Equal(AppointmentStatus.Expired, entry.After.Status);
        Assert.Equal(before.ReservationExpiresAt, entry.OccurredAt);
        Assert.Equal(before.StartsAt, entry.After.StartsAt);
        Assert.Equal(before.EndsAt, entry.After.EndsAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledByBusiness)]
    public void Cancelled_ShouldNormalizeReasonAndPreserveSchedule(
        AppointmentStatus status)
    {
        var before = CreateState();
        var after = before with { Status = status };

        var entry = CreateEntry(
            AppointmentHistoryType.Cancelled,
            before,
            after,
            reason: "  Cliente solicitou  ");

        Assert.Equal("Cliente solicitou", entry.Reason);
        Assert.Equal(before, entry.Before);
        Assert.Equal(after, entry.After);
        Assert.Equal(before.StartsAt, entry.After.StartsAt);
        Assert.Equal(before.EndsAt, entry.After.EndsAt);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(30)]
    public void Rescheduled_ShouldRespectExistingReservationDeadline(
        int minutesUntilStart)
    {
        var before = CreateState();
        var startsAt = CreatedAt.AddMinutes(minutesUntilStart);

        var after = before with
        {
            StartsAt = startsAt,
            EndsAt = startsAt.AddMinutes(45),
            ReservationExpiresAt = minutesUntilStart < 10
                ? startsAt
                : before.ReservationExpiresAt
        };

        var entry = CreateEntry(
            AppointmentHistoryType.Rescheduled,
            before,
            after);

        Assert.Equal(after.ReservationExpiresAt, entry.After.ReservationExpiresAt);
        Assert.Equal(CreatedAt.AddMinutes(10), entry.Before!.ReservationExpiresAt);
    }

    [Theory]
    [InlineData("missing-before")]
    [InlineData("unchanged")]
    [InlineData("different-status")]
    [InlineData("different-duration")]
    [InlineData("extended-expiration")]
    [InlineData("overdue-reservation")]
    public void Rescheduled_WithInvalidTransition_ShouldReject(
        string scenario)
    {
        var before = CreateState();

        var after = before with
        {
            StartsAt = before.StartsAt.AddHours(1),
            EndsAt = before.EndsAt.AddHours(1)
        };

        var now = CreatedAt.AddMinutes(1);

        switch (scenario)
        {
            case "unchanged":
                after = before;
                break;

            case "different-status":
                after = after with { Status = AppointmentStatus.Confirmed };
                break;

            case "different-duration":
                after = after with { EndsAt = after.EndsAt.AddMinutes(1) };
                break;

            case "extended-expiration":
                after = after with
                {
                    ReservationExpiresAt =
                        before.ReservationExpiresAt!.Value.AddMinutes(1)
                };
                break;

            case "overdue-reservation":
                now = before.ReservationExpiresAt!.Value;
                break;
        }

        Assert.Throws<ArgumentException>(() => CreateEntry(
            AppointmentHistoryType.Rescheduled,
            scenario == "missing-before" ? null : before,
            after,
            occurredAt: now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancelled_WithMissingReason_ShouldReject(string? reason)
    {
        var before = CreateState();

        Assert.Throws<ArgumentException>(() => CreateEntry(
            AppointmentHistoryType.Cancelled,
            before,
            before with { Status = AppointmentStatus.CancelledByClient },
            reason: reason));
    }

    [Theory]
    [InlineData(499)]
    [InlineData(500)]
    [InlineData(501)]
    public void Cancelled_ShouldValidateNormalizedReasonLength(int length)
    {
        var before = CreateState();
        var reason = $"  {new string('a', length)}  ";

        if (length > 500)
        {
            Assert.Throws<ArgumentException>(() => CreateEntry(
                AppointmentHistoryType.Cancelled,
                before,
                before with { Status = AppointmentStatus.CancelledByClient },
                reason: reason));

            return;
        }

        var entry = CreateEntry(
            AppointmentHistoryType.Cancelled,
            before,
            before with { Status = AppointmentStatus.CancelledByClient },
            reason: reason);

        Assert.Equal(length, entry.Reason!.Length);
    }

    [Theory]
    [InlineData("before-deadline")]
    [InlineData("different-location")]
    [InlineData("different-professional")]
    [InlineData("different-window")]
    public void Expired_WithInvalidTransition_ShouldReject(string scenario)
    {
        var before = CreateState();
        var after = before with { Status = AppointmentStatus.Expired };
        var now = before.ReservationExpiresAt!.Value;

        switch (scenario)
        {
            case "before-deadline":
                now = now.AddTicks(-10);
                break;

            case "different-location":
                after = after with { LocationId = Guid.NewGuid() };
                break;

            case "different-professional":
                after = after with { ProfessionalId = Guid.NewGuid() };
                break;

            case "different-window":
                after = after with
                {
                    StartsAt = after.StartsAt.AddMinutes(15),
                    EndsAt = after.EndsAt.AddMinutes(15)
                };
                break;
        }

        Assert.Throws<ArgumentException>(() => CreateEntry(
            AppointmentHistoryType.Expired,
            before,
            after,
            occurredAt: now));
    }

    #endregion

    #region Helpers

    private static AppointmentHistoryState CreateState()
    {
        var startsAt = CreatedAt.AddHours(2);

        return new AppointmentHistoryState(
            AppointmentStatus.Pending,
            Guid.NewGuid(),
            Guid.NewGuid(),
            startsAt,
            startsAt.AddMinutes(45),
            CreatedAt.AddMinutes(10));
    }

    private static AppointmentHistoryEntry CreateEntry(
        AppointmentHistoryType type,
        AppointmentHistoryState? before,
        AppointmentHistoryState after,
        DateTimeOffset? occurredAt = null,
        string? reason = null)
    {
        return new AppointmentHistoryEntry(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            occurredAt ?? CreatedAt.AddMinutes(1),
            before,
            after,
            reason);
    }

    #endregion
}