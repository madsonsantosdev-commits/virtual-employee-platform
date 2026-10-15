using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class ScheduleBlockTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_ShouldPreserveScopeAndNormalizeInstants(
        bool professionalScoped)
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        Guid? professionalId = professionalScoped ? Guid.NewGuid() : null;

        var startsAt = new DateTimeOffset(
            2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3));

        var endsAt = startsAt.AddHours(1);

        var block = new ScheduleBlock(
            id,
            tenantId,
            locationId,
            professionalId,
            startsAt,
            endsAt,
            CreatedAt.ToOffset(TimeSpan.FromHours(-3)));

        Assert.Equal(id, block.Id);
        Assert.Equal(tenantId, block.TenantId);
        Assert.Equal(locationId, block.LocationId);
        Assert.Equal(professionalId, block.ProfessionalId);
        Assert.True(block.IsActive);

        Assert.Equal(
            new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero),
            block.StartsAt);

        Assert.Equal(block.StartsAt.AddHours(1), block.EndsAt);
        Assert.Equal(TimeSpan.Zero, block.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, block.EndsAt.Offset);
        Assert.Equal(TimeSpan.Zero, block.CreatedAt.Offset);
        Assert.Equal(CreatedAt, block.CreatedAt);
        Assert.Equal(CreatedAt, block.UpdatedAt);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("tenant")]
    [InlineData("location")]
    [InlineData("professional")]
    public void Constructor_WithEmptyId_ShouldThrow(string field)
    {
        Assert.Throws<ArgumentException>(() =>
            new ScheduleBlock(
                field == "id" ? Guid.Empty : Guid.NewGuid(),
                field == "tenant" ? Guid.Empty : Guid.NewGuid(),
                field == "location" ? Guid.Empty : Guid.NewGuid(),
                field == "professional" ? Guid.Empty : Guid.NewGuid(),
                CreatedAt,
                CreatedAt.AddHours(1),
                CreatedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidInterval_ShouldThrow(int hours)
    {
        Assert.Throws<ArgumentException>(() =>
            CreateBlock(CreatedAt, CreatedAt.AddHours(hours)));
    }

    [Fact]
    public void Constructor_ShouldAllowBlockAcrossDays()
    {
        var start = new DateTimeOffset(
            2026, 10, 19, 22, 0, 0, TimeSpan.Zero);

        var block = CreateBlock(start, start.AddHours(8));

        Assert.Equal(start.AddHours(8), block.EndsAt);
        Assert.Equal(20, block.EndsAt.Day);
    }

    [Theory]
    [InlineData(-60, 0, false)]
    [InlineData(60, 120, false)]
    [InlineData(-30, 30, true)]
    [InlineData(30, 90, true)]
    [InlineData(15, 45, true)]
    [InlineData(-30, 90, true)]
    [InlineData(0, 60, true)]
    public void Overlaps_ShouldRespectExclusiveEnd(
        int startMinutes,
        int endMinutes,
        bool expected)
    {
        var block = CreateBlock(CreatedAt, CreatedAt.AddHours(1));

        var overlaps = block.Overlaps(
            CreatedAt.AddMinutes(startMinutes),
            CreatedAt.AddMinutes(endMinutes));

        Assert.Equal(expected, overlaps);
    }

    [Fact]
    public void Overlaps_ShouldCompareInstantsAcrossOffsets()
    {
        var block = CreateBlock(CreatedAt, CreatedAt.AddHours(1));

        var start = CreatedAt.AddMinutes(30)
            .ToOffset(TimeSpan.FromHours(-3));

        var end = CreatedAt.AddMinutes(90)
            .ToOffset(TimeSpan.FromHours(2));

        Assert.True(block.Overlaps(start, end));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Overlaps_WithInvalidInterval_ShouldThrow(int hours)
    {
        var block = CreateBlock(CreatedAt, CreatedAt.AddHours(1));

        Assert.Throws<ArgumentException>(() =>
            block.Overlaps(
                CreatedAt,
                CreatedAt.AddHours(hours)));
    }

    [Fact]
    public void Deactivate_ShouldPreserveDataAndIgnoreRepeatedDeactivation()
    {
        var block = CreateBlock(CreatedAt, CreatedAt.AddHours(1));
        var id = block.Id;
        var deactivatedAt = CreatedAt.AddHours(2);

        block.Deactivate(
            deactivatedAt.ToOffset(TimeSpan.FromHours(-3)));

        Assert.False(block.IsActive);
        Assert.Equal(id, block.Id);
        Assert.Equal(CreatedAt, block.StartsAt);
        Assert.Equal(CreatedAt.AddHours(1), block.EndsAt);
        Assert.Equal(CreatedAt, block.CreatedAt);
        Assert.Equal(deactivatedAt, block.UpdatedAt);
        Assert.Equal(TimeSpan.Zero, block.UpdatedAt.Offset);

        Assert.False(
            block.Overlaps(CreatedAt, CreatedAt.AddMinutes(30)));

        block.Deactivate(CreatedAt.AddHours(3));

        Assert.Equal(deactivatedAt, block.UpdatedAt);
    }

    private static ScheduleBlock CreateBlock(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        return new ScheduleBlock(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            startsAt,
            endsAt,
            CreatedAt);
    }
}