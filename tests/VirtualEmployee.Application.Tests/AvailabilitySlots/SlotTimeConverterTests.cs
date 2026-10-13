using VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;
using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Application.Tests.AvailabilitySlots;

public sealed class SlotTimeConverterTests
{
    [Fact]
    public void TryConvert_ShouldConvertLocalTimesToUtc()
    {
        var professionalId = Guid.NewGuid();
        var timezone = TimeZoneInfo.CreateCustomTimeZone(
            "TestMinusThree",
            TimeSpan.FromHours(-3),
            "TestMinusThree",
            "TestMinusThree");

        var result = SlotTimeConverter.TryConvert(
            professionalId,
            Slot(2026, 10, 19, 9, 0, 10, 0),
            timezone);

        Assert.NotNull(result);
        Assert.Equal(professionalId, result.ProfessionalId);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            result.StartsAt);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 13, 0, 0, TimeSpan.Zero),
            result.EndsAt);
        Assert.Equal(TimeSpan.Zero, result.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, result.EndsAt.Offset);
    }

    [Fact]
    public void TryConvert_ShouldAllowUtcDateToDifferFromLocalDate()
    {
        var timezone = TimeZoneInfo.CreateCustomTimeZone(
            "TestPlusThree",
            TimeSpan.FromHours(3),
            "TestPlusThree",
            "TestPlusThree");

        var result = SlotTimeConverter.TryConvert(
            Guid.NewGuid(),
            Slot(2026, 10, 19, 0, 0, 1, 0),
            timezone);

        Assert.NotNull(result);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 18, 21, 0, 0, TimeSpan.Zero),
            result.StartsAt);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 18, 22, 0, 0, TimeSpan.Zero),
            result.EndsAt);
    }

    [Theory]
    [InlineData(3, 8, 2, 15, 3, 15)]
    [InlineData(3, 8, 1, 30, 2, 30)]
    [InlineData(11, 1, 1, 15, 2, 15)]
    [InlineData(11, 1, 0, 30, 1, 30)]
    public void TryConvert_WithInvalidOrAmbiguousEndpoint_ShouldReturnNull(
        int month,
        int day,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute)
    {
        var result = SlotTimeConverter.TryConvert(
            Guid.NewGuid(),
            Slot(
                2026, month, day,
                startHour, startMinute,
                endHour, endMinute),
            CreateDaylightTimezone());

        Assert.Null(result);
    }

    [Theory]
    [InlineData(3, 8)]
    [InlineData(11, 1)]
    public void TryConvert_WhenTransitionChangesDuration_ShouldReturnNull(
        int month,
        int day)
    {
        var result = SlotTimeConverter.TryConvert(
            Guid.NewGuid(),
            Slot(2026, month, day, 0, 30, 3, 30),
            CreateDaylightTimezone());

        Assert.Null(result);
    }

    [Fact]
    public void TryConvert_WithNormalDaylightTime_ShouldPreserveDuration()
    {
        var result = SlotTimeConverter.TryConvert(
            Guid.NewGuid(),
            Slot(2026, 7, 1, 9, 0, 10, 0),
            CreateDaylightTimezone());

        Assert.NotNull(result);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 1, 13, 0, 0, TimeSpan.Zero),
            result.StartsAt);
        Assert.Equal(TimeSpan.FromHours(1), result.EndsAt - result.StartsAt);
    }

    private static LocalSlot Slot(
        int year,
        int month,
        int day,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute)
    {
        return new LocalSlot(
            new DateTime(
                year, month, day,
                startHour, startMinute, 0,
                DateTimeKind.Unspecified),
            new DateTime(
                year, month, day,
                endHour, endMinute, 0,
                DateTimeKind.Unspecified));
    }

    private static TimeZoneInfo CreateDaylightTimezone()
    {
        var transitionTime = new DateTime(1, 1, 1, 2, 0, 0);

        var start = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            transitionTime,
            month: 3,
            day: 8);

        var end = TimeZoneInfo.TransitionTime.CreateFixedDateRule(
            transitionTime,
            month: 11,
            day: 1);

        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            start,
            end);

        return TimeZoneInfo.CreateCustomTimeZone(
            "TestDaylight",
            TimeSpan.FromHours(-5),
            "TestDaylight",
            "TestStandard",
            "TestSummer",
            [rule]);
    }
}