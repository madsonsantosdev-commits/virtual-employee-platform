using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class SlotCalculatorTests
{
    private static readonly DateOnly Date = new(2026, 10, 19);

    [Fact]
    public void Calculate_ShouldRespectDurationAndInterval()
    {
        var slots = SlotCalculator.Calculate(
            Date,
            [(new TimeOnly(9, 0), new TimeOnly(12, 0))],
            durationMinutes: 60,
            slotIntervalMinutes: 20);

        Assert.Equal(
            new[]
            {
                new TimeOnly(9, 0),
                new TimeOnly(9, 20),
                new TimeOnly(9, 40),
                new TimeOnly(10, 0),
                new TimeOnly(10, 20),
                new TimeOnly(10, 40),
                new TimeOnly(11, 0)
            },
            slots.Select(slot => TimeOnly.FromDateTime(slot.StartsAt))
                .ToArray());

        Assert.All(slots, slot =>
        {
            Assert.Equal(Date, DateOnly.FromDateTime(slot.StartsAt));
            Assert.Equal(DateTimeKind.Unspecified, slot.StartsAt.Kind);
            Assert.Equal(DateTimeKind.Unspecified, slot.EndsAt.Kind);
            Assert.Equal(TimeSpan.FromHours(1), slot.EndsAt - slot.StartsAt);
        });

        Assert.Equal(
            Date.ToDateTime(new TimeOnly(12, 0)),
            slots[^1].EndsAt);
    }

    [Fact]
    public void Calculate_ShouldPreserveBreaksAndOrderWindows()
    {
        var slots = SlotCalculator.Calculate(
            Date,
            [
                (new TimeOnly(13, 0), new TimeOnly(14, 0)),
                (new TimeOnly(9, 0), new TimeOnly(10, 0))
            ],
            durationMinutes: 60,
            slotIntervalMinutes: 15);

        Assert.Equal(2, slots.Count);
        Assert.Equal(Date.ToDateTime(new TimeOnly(9, 0)), slots[0].StartsAt);
        Assert.Equal(Date.ToDateTime(new TimeOnly(10, 0)), slots[0].EndsAt);
        Assert.Equal(Date.ToDateTime(new TimeOnly(13, 0)), slots[1].StartsAt);
        Assert.Equal(Date.ToDateTime(new TimeOnly(14, 0)), slots[1].EndsAt);
    }

    [Fact]
    public void Calculate_ShouldMergeAdjacentWindows()
    {
        var slots = SlotCalculator.Calculate(
            Date,
            [
                (new TimeOnly(9, 0), new TimeOnly(10, 0)),
                (new TimeOnly(10, 0), new TimeOnly(11, 0))
            ],
            durationMinutes: 90,
            slotIntervalMinutes: 30);

        Assert.Equal(2, slots.Count);
        Assert.Equal(Date.ToDateTime(new TimeOnly(9, 0)), slots[0].StartsAt);
        Assert.Equal(Date.ToDateTime(new TimeOnly(10, 30)), slots[0].EndsAt);
        Assert.Equal(Date.ToDateTime(new TimeOnly(9, 30)), slots[1].StartsAt);
        Assert.Equal(Date.ToDateTime(new TimeOnly(11, 0)), slots[1].EndsAt);
    }

    [Fact]
    public void Calculate_WithDurationLongerThanWindow_ShouldReturnEmpty()
    {
        var slots = SlotCalculator.Calculate(
            Date,
            [(new TimeOnly(23, 0), new TimeOnly(23, 30))],
            durationMinutes: 60,
            slotIntervalMinutes: 15);

        Assert.Empty(slots);
    }

    [Fact]
    public void Calculate_WithNoWindows_ShouldReturnEmpty()
    {
        var slots = SlotCalculator.Calculate(
            Date,
            [],
            durationMinutes: 30,
            slotIntervalMinutes: 15);

        Assert.Empty(slots);
    }

    [Theory]
    [InlineData(0, 15)]
    [InlineData(-1, 15)]
    [InlineData(30, 0)]
    [InlineData(30, -1)]
    public void Calculate_WithInvalidDurationOrInterval_ShouldThrow(
        int durationMinutes,
        int slotIntervalMinutes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SlotCalculator.Calculate(
                Date,
                [(new TimeOnly(9, 0), new TimeOnly(12, 0))],
                durationMinutes,
                slotIntervalMinutes));
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(12, 9)]
    public void Calculate_WithInvalidWindow_ShouldThrow(
        int startHour,
        int endHour)
    {
        Assert.Throws<ArgumentException>(() =>
            SlotCalculator.Calculate(
                Date,
                [(new TimeOnly(startHour, 0), new TimeOnly(endHour, 0))],
                durationMinutes: 30,
                slotIntervalMinutes: 15));
    }
}