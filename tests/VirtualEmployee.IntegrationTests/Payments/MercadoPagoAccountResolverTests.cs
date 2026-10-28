using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoAccountResolverTests
{
    [Fact]
    public void ResolveAccessToken_ShouldSelectExactTenantAndBusiness()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var resolver = CreateResolver(
            new MercadoPagoAccountOptions
            {
                TenantId = Guid.NewGuid(),
                BusinessId = businessId,
                AccessToken = "other-tenant-token"
            },
            new MercadoPagoAccountOptions
            {
                TenantId = tenantId,
                BusinessId = Guid.NewGuid(),
                AccessToken = "other-business-token"
            },
            new MercadoPagoAccountOptions
            {
                TenantId = tenantId,
                BusinessId = businessId,
                AccessToken = "expected-token"
            });

        var token = resolver.ResolveAccessToken(
            tenantId,
            businessId);

        Assert.Equal("expected-token", token);
    }

    [Fact]
    public void ResolveAccessToken_WithForeignTenant_ShouldReject()
    {
        var businessId = Guid.NewGuid();

        var resolver = CreateResolver(
            new MercadoPagoAccountOptions
            {
                TenantId = Guid.NewGuid(),
                BusinessId = businessId,
                AccessToken = "foreign-token"
            });

        Assert.Throws<InvalidOperationException>(() =>
            resolver.ResolveAccessToken(
                Guid.NewGuid(),
                businessId));
    }

    [Fact]
    public void ResolveAccessToken_WithDifferentBusiness_ShouldReject()
    {
        var tenantId = Guid.NewGuid();

        var resolver = CreateResolver(
            new MercadoPagoAccountOptions
            {
                TenantId = tenantId,
                BusinessId = Guid.NewGuid(),
                AccessToken = "another-business-token"
            });

        Assert.Throws<InvalidOperationException>(() =>
            resolver.ResolveAccessToken(
                tenantId,
                Guid.NewGuid()));
    }

    [Fact]
    public void ResolveAccessToken_WithDuplicateAccounts_ShouldReject()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var resolver = CreateResolver(
            new MercadoPagoAccountOptions
            {
                TenantId = tenantId,
                BusinessId = businessId,
                AccessToken = "first-token"
            },
            new MercadoPagoAccountOptions
            {
                TenantId = tenantId,
                BusinessId = businessId,
                AccessToken = "second-token"
            });

        Assert.Throws<InvalidOperationException>(() =>
            resolver.ResolveAccessToken(
                tenantId,
                businessId));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("token with spaces")]
    [InlineData("token\tvalue")]
    [InlineData("token\nvalue")]
    public void ResolveAccessToken_WithInvalidToken_ShouldReject(
        string accessToken)
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var resolver = CreateResolver(
            new MercadoPagoAccountOptions
            {
                TenantId = tenantId,
                BusinessId = businessId,
                AccessToken = accessToken
            });

        Assert.Throws<InvalidOperationException>(() =>
            resolver.ResolveAccessToken(
                tenantId,
                businessId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResolveAccessToken_WithEmptyIdentifier_ShouldReject(
        bool emptyTenant)
    {
        var resolver = CreateResolver();

        var exception = Assert.Throws<ArgumentException>(() =>
            resolver.ResolveAccessToken(
                emptyTenant ? Guid.Empty : Guid.NewGuid(),
                emptyTenant ? Guid.NewGuid() : Guid.Empty));

        Assert.Equal(
            emptyTenant ? "tenantId" : "businessId",
            exception.ParamName);
    }

    private static MercadoPagoAccountResolver CreateResolver(
        params MercadoPagoAccountOptions[] accounts)
    {
        return new MercadoPagoAccountResolver(
            new MercadoPagoOptions
            {
                Accounts = accounts.ToList()
            });
    }
}