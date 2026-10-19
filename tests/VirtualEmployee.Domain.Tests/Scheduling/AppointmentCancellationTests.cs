using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AppointmentCancellationTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    #region Facts

    [Fact]
    public void Cancel_ShouldNormalizeTimestampToUtc()
    {
        var appointment = CreateAppointment();

        var now = new DateTimeOffset(
            2026, 10, 19, 9, 5, 0, TimeSpan.FromHours(-3));

        appointment.Cancel(
            AppointmentStatus.CancelledByClient,
            "Cliente solicitou",
            now);

        Assert.Equal(now.ToUniversalTime(), appointment.CancelledAt);
        Assert.Equal(TimeSpan.Zero, appointment.CancelledAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, appointment.UpdatedAt.Offset);
    }

    [Fact]
    public void Cancel_WhenRepeated_ShouldPreserveOriginalReasonAndAudit()
    {
        var appointment = CreateAppointment();
        var cancelledAt = CreatedAt.AddMinutes(2);

        Assert.True(appointment.Cancel(
            AppointmentStatus.CancelledByClient,
            "Motivo original",
            cancelledAt));

        var changed = appointment.Cancel(
            AppointmentStatus.CancelledByClient,
            "Outro motivo",
            CreatedAt.AddMinutes(3));

        Assert.False(changed);
        Assert.Equal("Motivo original", appointment.CancellationReason);
        Assert.Equal(cancelledAt, appointment.CancelledAt);
        Assert.Equal(cancelledAt, appointment.UpdatedAt);
    }

    [Fact]
    public void Cancel_WithDifferentCancellationOrigin_ShouldReject()
    {
        var appointment = CreateAppointment();

        appointment.Cancel(
            AppointmentStatus.CancelledByClient,
            "Cliente solicitou",
            CreatedAt.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(() =>
            appointment.Cancel(
                AppointmentStatus.CancelledByBusiness,
                "Estabelecimento solicitou",
                CreatedAt.AddMinutes(3)));

        Assert.Equal(
            AppointmentStatus.CancelledByClient,
            appointment.Status);
        Assert.Equal("Cliente solicitou", appointment.CancellationReason);
        Assert.Equal(CreatedAt.AddMinutes(2), appointment.CancelledAt);
    }

    [Fact]
    public void Cancel_AfterExpiration_ShouldReject()
    {
        var appointment = CreateAppointment();

        Assert.True(appointment.Expire(CreatedAt.AddMinutes(10)));

        Assert.Throws<InvalidOperationException>(() =>
            appointment.Cancel(
                AppointmentStatus.CancelledByClient,
                "Cliente solicitou",
                CreatedAt.AddMinutes(11)));

        Assert.Equal(AppointmentStatus.Expired, appointment.Status);
        Assert.Null(appointment.CancellationReason);
        Assert.Null(appointment.CancelledAt);
    }

    [Fact]
    public void Expire_AfterCancellation_ShouldPreserveCancellation()
    {
        var appointment = CreateAppointment();
        var cancelledAt = CreatedAt.AddMinutes(2);

        appointment.Cancel(
            AppointmentStatus.CancelledByBusiness,
            "Profissional indisponível",
            cancelledAt);

        Assert.False(appointment.Expire(CreatedAt.AddMinutes(11)));

        Assert.Equal(
            AppointmentStatus.CancelledByBusiness,
            appointment.Status);
        Assert.Equal("Profissional indisponível", appointment.CancellationReason);
        Assert.Equal(cancelledAt, appointment.CancelledAt);
        Assert.Equal(cancelledAt, appointment.UpdatedAt);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(500)]
    [InlineData(501)]
    public void Cancel_ShouldValidateNormalizedReasonLength(int length)
    {
        var appointment = CreateAppointment();
        var reason = "  " + new string('a', length) + "  ";
        var now = CreatedAt.AddMinutes(1);

        if (length == 500)
        {
            Assert.True(appointment.Cancel(
                AppointmentStatus.CancelledByClient,
                reason,
                now));

            Assert.Equal(
                new string('a', length),
                appointment.CancellationReason);
            Assert.Equal(now, appointment.CancelledAt);
        }
        else
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
                appointment.Cancel(
                    AppointmentStatus.CancelledByClient,
                    reason,
                    now));

            Assert.Equal("reason", exception.ParamName);
            Assert.Equal(AppointmentStatus.Pending, appointment.Status);
            Assert.Null(appointment.CancellationReason);
            Assert.Null(appointment.CancelledAt);
            Assert.Equal(CreatedAt, appointment.UpdatedAt);
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.CancelledByClient)]
    [InlineData(AppointmentStatus.CancelledByBusiness)]
    public void Cancel_WithValidOrigin_ShouldReleaseScheduleAndPreserveSnapshots(
        AppointmentStatus cancellationStatus)
    {
        var appointment = CreateAppointment();
        var originalItem = Assert.Single(appointment.Items);
        var originalStartsAt = appointment.StartsAt;
        var originalEndsAt = appointment.EndsAt;
        var originalExpiration = appointment.ReservationExpiresAt;
        var now = CreatedAt.AddMinutes(5);

        var changed = appointment.Cancel(
            cancellationStatus,
            "  Solicitação de cancelamento  ",
            now);

        Assert.True(changed);
        Assert.Equal(cancellationStatus, appointment.Status);
        Assert.False(appointment.OccupiesSchedule);
        Assert.Equal(
            "Solicitação de cancelamento",
            appointment.CancellationReason);
        Assert.Equal(now, appointment.CancelledAt);
        Assert.Equal(now, appointment.UpdatedAt);
        Assert.Equal(CreatedAt, appointment.CreatedAt);

        Assert.Equal(originalStartsAt, appointment.StartsAt);
        Assert.Equal(originalEndsAt, appointment.EndsAt);
        Assert.Equal(originalExpiration, appointment.ReservationExpiresAt);
        Assert.Equal(65m, appointment.TotalPriceSnapshot);
        Assert.Equal(45, appointment.TotalDurationMinutesSnapshot);

        var item = Assert.Single(appointment.Items);

        Assert.Same(originalItem, item);
        Assert.Equal("Corte", item.ServiceNameSnapshot);
        Assert.Equal(65m, item.PriceSnapshot);
        Assert.Equal(45, item.DurationMinutesSnapshot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Cancel_WithBlankReason_ShouldReject(string? reason)
    {
        var appointment = CreateAppointment();

        var exception = Assert.Throws<ArgumentException>(() =>
            appointment.Cancel(
                AppointmentStatus.CancelledByClient,
                reason!,
                CreatedAt.AddMinutes(1)));

        Assert.Equal("reason", exception.ParamName);
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Null(appointment.CancellationReason);
        Assert.Null(appointment.CancelledAt);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.ConfirmedByClient)]
    [InlineData(AppointmentStatus.RescheduleRequested)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Expired)]
    [InlineData((AppointmentStatus)999)]
    public void Cancel_WithInvalidDestination_ShouldReject(
        AppointmentStatus destination)
    {
        var appointment = CreateAppointment();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            appointment.Cancel(
                destination,
                "Motivo",
                CreatedAt.AddMinutes(1)));

        Assert.Equal("cancellationStatus", exception.ParamName);
        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Null(appointment.CancelledAt);
        Assert.Null(appointment.CancellationReason);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    public void Cancel_WithOverduePendingReservation_ShouldReject(
        int elapsedMinutes)
    {
        var appointment = CreateAppointment();

        Assert.Throws<InvalidOperationException>(() =>
            appointment.Cancel(
                AppointmentStatus.CancelledByClient,
                "Motivo",
                CreatedAt.AddMinutes(elapsedMinutes)));

        Assert.Equal(AppointmentStatus.Pending, appointment.Status);
        Assert.Null(appointment.CancelledAt);
        Assert.Null(appointment.CancellationReason);
        Assert.Equal(CreatedAt, appointment.UpdatedAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancel_WithTimestampBeforeLastUpdate_ShouldReject(
        bool alreadyCancelled)
    {
        var appointment = CreateAppointment();

        if (alreadyCancelled)
        {
            appointment.Cancel(
                AppointmentStatus.CancelledByClient,
                "Motivo original",
                CreatedAt.AddMinutes(2));
        }

        var originalStatus = appointment.Status;
        var originalUpdatedAt = appointment.UpdatedAt;
        var originalCancelledAt = appointment.CancelledAt;
        var originalReason = appointment.CancellationReason;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            appointment.Cancel(
                AppointmentStatus.CancelledByClient,
                "Motivo",
                originalUpdatedAt.AddTicks(-1)));

        Assert.Equal("now", exception.ParamName);
        Assert.Equal(originalStatus, appointment.Status);
        Assert.Equal(originalUpdatedAt, appointment.UpdatedAt);
        Assert.Equal(originalCancelledAt, appointment.CancelledAt);
        Assert.Equal(originalReason, appointment.CancellationReason);
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