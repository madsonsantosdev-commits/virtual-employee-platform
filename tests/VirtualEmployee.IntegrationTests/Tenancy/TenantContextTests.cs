using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Infrastructure;
using VirtualEmployee.Infrastructure.Tenancy;

namespace VirtualEmployee.IntegrationTests.Tenancy;

public sealed class TenantContextTests
{
    [Fact]
    public void TenantId_WhenNotInitialized_ShouldThrow()
    {
        var context = new TenantContext();

        Assert.False(context.IsInitialized);

        Assert.Throws<InvalidOperationException>(
            () => context.TenantId);
    }

    [Fact]
    public void Initialize_ShouldSetTenantId()
    {
        var tenantId = Guid.NewGuid();
        var context = new TenantContext();

        context.Initialize(tenantId);

        Assert.True(context.IsInitialized);
        Assert.Equal(tenantId, context.TenantId);
    }

    [Fact]
    public void Initialize_WhenAlreadyInitialized_ShouldThrow()
    {
        var context = new TenantContext();

        context.Initialize(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(
            () => context.Initialize(Guid.NewGuid()));
    }

    [Fact]
    public void ScopedContracts_ShouldShareSameTenantContext()
    {
        var tenantId = Guid.NewGuid();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] =
                    "Host=localhost;Database=test;Username=test;Password=test"
            })
            .Build();

        var services = new ServiceCollection();

        services.AddInfrastructure(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var initializer =
            scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>();

        var context =
            scope.ServiceProvider.GetRequiredService<ITenantContext>();

        initializer.Initialize(tenantId);

        Assert.True(context.IsInitialized);
        Assert.Equal(tenantId, context.TenantId);
    }
}