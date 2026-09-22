using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VirtualEmployee.Domain.Common;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Tenancy;

namespace VirtualEmployee.ArchitectureTests.Persistence;

public sealed class TenantIsolationArchitectureTests
{
    [Fact]
    public void TenantScopedEntities_ShouldHaveQueryFilter()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=architecture_tests;Username=test;Password=test")
            .Options;

        var tenantContext = new TenantContext();
        tenantContext.Initialize(Guid.NewGuid());

        using var dbContext = new AppDbContext(
            options,
            tenantContext);

        var tenantScopedEntities = dbContext.Model
            .GetEntityTypes()
            .Where(IsTenantScopedEntity)
            .ToList();

        Assert.NotEmpty(tenantScopedEntities);

        foreach (var entityType in tenantScopedEntities)
        {
            Assert.True(
                entityType.GetDeclaredQueryFilters().Any(),
                $"Entity '{entityType.ClrType.Name}' implements " +
                $"{nameof(ITenantScoped)} but does not have a query filter.");
        }
    }

    private static bool IsTenantScopedEntity(
        IReadOnlyEntityType entityType)
    {
        return typeof(ITenantScoped)
            .IsAssignableFrom(entityType.ClrType);
    }
}