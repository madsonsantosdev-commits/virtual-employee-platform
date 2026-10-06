using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Domain.Businesses;
using VirtualEmployee.Domain.Common;
using VirtualEmployee.Domain.Tenants;
using VirtualEmployee.Domain.BusinessTypes;
using VirtualEmployee.Domain.Locations;
using VirtualEmployee.Domain.Services;
using VirtualEmployee.Domain.Professionals;

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
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceComponent> ServiceComponents => Set<ServiceComponent>();
    public DbSet<LocationService> LocationServices => Set<LocationService>();
    public DbSet<Professional> Professionals => Set<Professional>();
    public DbSet<ProfessionalLocation> ProfessionalLocations => Set<ProfessionalLocation>();
    public Guid CurrentTenantId => _tenantContext.TenantId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);

        modelBuilder.Entity<Business>()
            .HasQueryFilter(
                business => business.TenantId == CurrentTenantId);

        modelBuilder.Entity<Location>()
            .HasQueryFilter(
                location => location.TenantId == CurrentTenantId);

        modelBuilder.Entity<Service>()
            .HasQueryFilter(
                service => service.TenantId == CurrentTenantId);

        modelBuilder.Entity<ServiceComponent>()
            .HasQueryFilter(
                component => component.TenantId == CurrentTenantId);

        modelBuilder.Entity<LocationService>()
            .HasQueryFilter(
                locationService =>
                    locationService.TenantId == CurrentTenantId);

        modelBuilder.Entity<Professional>()
            .HasQueryFilter(
                professional =>
                    professional.TenantId == CurrentTenantId);

        modelBuilder.Entity<ProfessionalLocation>()
            .HasQueryFilter(
                professionalLocation =>
                    professionalLocation.TenantId == CurrentTenantId);

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
        var entries = ChangeTracker
            .Entries<ITenantScoped>()
            .Where(entry =>
                entry.State is EntityState.Added
                    or EntityState.Modified
                    or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        var tenantId = CurrentTenantId;

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