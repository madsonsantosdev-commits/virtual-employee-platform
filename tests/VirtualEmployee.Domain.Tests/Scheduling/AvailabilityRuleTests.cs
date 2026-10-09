using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AvailabilityRuleTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WithValidData_ShouldCreateActiveRule()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();

        var rule = new AvailabilityRule(
            id,
            tenantId,
            locationId,
            professionalId,
            DayOfWeek.Monday,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0),
            CreatedAt);

        Assert.Equal(id, rule.Id);
        Assert.Equal(tenantId, rule.TenantId);
        Assert.Equal(locationId, rule.LocationId);
        Assert.Equal(professionalId, rule.ProfessionalId);
        Assert.Equal(DayOfWeek.Monday, rule.DayOfWeek);
        Assert.Equal(new TimeOnly(9, 0), rule.StartTime);
        Assert.Equal(new TimeOnly(12, 0), rule.EndTime);
        Assert.True(rule.IsActive);
        Assert.Equal(CreatedAt, rule.CreatedAt);
        Assert.Equal(CreatedAt, rule.UpdatedAt);
    }

    [Theory]
    [InlineData(0, "id")]
    [InlineData(1, "tenantId")]
    [InlineData(2, "locationId")]
    [InlineData(3, "professionalId")]
    public void Constructor_WithEmptyId_ShouldThrow(
        int emptyIndex,
        string parameterName)
    {
        var ids = new[]
        {
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid()
        };

        ids[emptyIndex] = Guid.Empty;

        var exception = Assert.Throws<ArgumentException>(() =>
            new AvailabilityRule(
                ids[0],
                ids[1],
                ids[2],
                ids[3],
                DayOfWeek.Monday,
                new TimeOnly(9, 0),
                new TimeOnly(12, 0),
                CreatedAt));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Constructor_WithInvalidDay_ShouldThrow(int day)
    {
        var exception =
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CreateRule((DayOfWeek)day));

        Assert.Equal("dayOfWeek", exception.ParamName);
    }

    [Theory]
    [InlineData(9, 9)]
    [InlineData(12, 9)]
    [InlineData(22, 2)]
    public void Constructor_WithInvalidWindow_ShouldThrow(
        int startHour,
        int endHour)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AvailabilityRule(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                DayOfWeek.Monday,
                new TimeOnly(startHour, 0),
                new TimeOnly(endHour, 0),
                CreatedAt));

        Assert.Equal("endTime", exception.ParamName);
    }

    [Fact]
    public void Update_WithChanges_ShouldPreserveIdentityAndCreatedAt()
    {
        var rule = CreateRule();
        var id = rule.Id;
        var tenantId = rule.TenantId;
        var locationId = rule.LocationId;
        var professionalId = rule.ProfessionalId;
        var updatedAt = CreatedAt.AddHours(1);

        rule.Update(
            DayOfWeek.Tuesday,
            new TimeOnly(13, 0),
            new TimeOnly(18, 0),
            false,
            updatedAt);

        Assert.Equal(id, rule.Id);
        Assert.Equal(tenantId, rule.TenantId);
        Assert.Equal(locationId, rule.LocationId);
        Assert.Equal(professionalId, rule.ProfessionalId);
        Assert.Equal(DayOfWeek.Tuesday, rule.DayOfWeek);
        Assert.Equal(new TimeOnly(13, 0), rule.StartTime);
        Assert.Equal(new TimeOnly(18, 0), rule.EndTime);
        Assert.False(rule.IsActive);
        Assert.Equal(CreatedAt, rule.CreatedAt);
        Assert.Equal(updatedAt, rule.UpdatedAt);
    }

    [Fact]
    public void Update_WithoutChanges_ShouldPreserveUpdatedAt()
    {
        var rule = CreateRule();

        rule.Update(
            rule.DayOfWeek,
            rule.StartTime,
            rule.EndTime,
            rule.IsActive,
            CreatedAt.AddHours(1));

        Assert.Equal(CreatedAt, rule.UpdatedAt);
    }

    [Theory]
    [InlineData(-1, 9, 12)]
    [InlineData(7, 9, 12)]
    [InlineData(1, 9, 9)]
    [InlineData(1, 12, 9)]
    public void Update_WithInvalidData_ShouldPreserveState(
        int day,
        int startHour,
        int endHour)
    {
        var rule = CreateRule();

        Assert.ThrowsAny<ArgumentException>(() =>
            rule.Update(
                (DayOfWeek)day,
                new TimeOnly(startHour, 0),
                new TimeOnly(endHour, 0),
                false,
                CreatedAt.AddHours(1)));

        Assert.Equal(DayOfWeek.Monday, rule.DayOfWeek);
        Assert.Equal(new TimeOnly(9, 0), rule.StartTime);
        Assert.Equal(new TimeOnly(12, 0), rule.EndTime);
        Assert.True(rule.IsActive);
        Assert.Equal(CreatedAt, rule.UpdatedAt);
    }

    private static AvailabilityRule CreateRule(
        DayOfWeek dayOfWeek = DayOfWeek.Monday)
    {
        return new AvailabilityRule(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            dayOfWeek,
            new TimeOnly(9, 0),
            new TimeOnly(12, 0),
            CreatedAt);
    }
}