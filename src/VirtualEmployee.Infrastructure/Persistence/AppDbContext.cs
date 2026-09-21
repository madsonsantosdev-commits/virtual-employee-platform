using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Common;
using VirtualEmployee.Domain.Tenants;

namespace VirtualEmployee.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Business> Businesses => Set<Business>();

    public Guid CurrentTenantId => _tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Business>()
            .HasQueryFilter(
                business => business.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }

    public override int SaveChanges(
        bool acceptAllChangesOnSuccess)
    {
        ValidateTenantOwnership();

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ValidateTenantOwnership();

        return base.SaveChangesAsync(
            acceptAllChangesOnSuccess,
            cancellationToken);
    }

    private void ValidateTenantOwnership()
    {
        var tenantId = CurrentTenantId;

        var entries = ChangeTracker
            .Entries<ITenantScoped>()
            .Where(entry =>
                entry.State is EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted);

        foreach (var entry in entries)
        {
            if (entry.Entity.TenantId != tenantId)
            {
                throw new InvalidOperationException(
                    "Cross-tenant data modification is not allowed.");
            }
        }
    }
}