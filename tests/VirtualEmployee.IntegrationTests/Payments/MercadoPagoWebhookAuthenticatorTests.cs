using System.Security.Cryptography;
using System.Text;
using VirtualEmployee.Infrastructure.Payments.MercadoPago;
using Xunit;

namespace VirtualEmployee.IntegrationTests.Payments;

public sealed class MercadoPagoWebhookAuthenticatorTests
{
    private const string RequestId =
        "11111111-2222-4333-8444-555555555555";

    private const string DataId = "ORDTST123";
    private const string Timestamp = "1793217600";

    #region Facts

    [Fact]
    public void Authenticate_WithValidSignature_ShouldReturnConfiguredAccount()
    {
        var account = CreateAccount();
        var authenticator = CreateAuthenticator(account);

        var result = authenticator.Authenticate(
            account.WebhookId,
            Sign(account.WebhookSecret),
            RequestId,
            DataId);

        Assert.NotNull(result);
        Assert.Equal(account.TenantId, result.TenantId);
        Assert.Equal(account.BusinessId, result.BusinessId);
        Assert.Equal(account.AccessToken, result.AccessToken);
        Assert.Equal(account.CollectorId, result.CollectorId);
        Assert.Equal(account.ApplicationId, result.ApplicationId);

        Assert.NotSame(account, result);
        Assert.Equal(string.Empty, result.WebhookSecret);
    }

    [Fact]
    public void Authenticate_WhenDisabled_ShouldReturnNull()
    {
        var account = CreateAccount();
        var options = new MercadoPagoOptions
        {
            WebhooksEnabled = false,
            Accounts = [account]
        };

        var authenticator = new MercadoPagoWebhookAuthenticator(
            options,
            new MercadoPagoAccountResolver(options));

        Assert.Null(authenticator.Authenticate(
            account.WebhookId,
            Sign(account.WebhookSecret),
            RequestId,
            DataId));
    }

    [Fact]
    public void Authenticate_WithUnknownWebhookId_ShouldReturnNull()
    {
        var account = CreateAccount();
        var authenticator = CreateAuthenticator(account);

        Assert.Null(authenticator.Authenticate(
            Guid.NewGuid(),
            Sign(account.WebhookSecret),
            RequestId,
            DataId));
    }

    [Fact]
    public void Authenticate_WithEmptyWebhookId_ShouldReturnNull()
    {
        var account = CreateAccount();
        var authenticator = CreateAuthenticator(account);

        Assert.Null(authenticator.Authenticate(
            Guid.Empty,
            Sign(account.WebhookSecret),
            RequestId,
            DataId));
    }

    [Fact]
    public void Authenticate_WithDuplicateWebhookId_ShouldThrow()
    {
        var first = CreateAccount();
        var second = CreateAccount();

        second.WebhookId = first.WebhookId;

        var authenticator = CreateAuthenticator(first, second);

        Assert.Throws<InvalidOperationException>(() =>
            authenticator.Authenticate(
                first.WebhookId,
                Sign(first.WebhookSecret),
                RequestId,
                DataId));
    }

    [Fact]
    public void Authenticate_WithAnotherAccountSecret_ShouldReturnNull()
    {
        var first = CreateAccount();
        var second = CreateAccount();

        second.WebhookSecret = "second-account-secret";

        var authenticator = CreateAuthenticator(first, second);

        Assert.Null(authenticator.Authenticate(
            second.WebhookId,
            Sign(first.WebhookSecret),
            RequestId,
            DataId));
    }

    [Fact]
    public void Authenticate_ShouldSelectAccountByWebhookId()
    {
        var first = CreateAccount();
        var second = CreateAccount();

        second.WebhookSecret = "second-account-secret";
        second.AccessToken = "second-business-token";
        second.CollectorId = 222222;
        second.ApplicationId = 333333;

        var authenticator = CreateAuthenticator(first, second);

        var result = authenticator.Authenticate(
            second.WebhookId,
            Sign(second.WebhookSecret),
            RequestId,
            DataId);

        Assert.NotNull(result);
        Assert.Equal(second.TenantId, result.TenantId);
        Assert.Equal(second.BusinessId, result.BusinessId);
        Assert.Equal(second.AccessToken, result.AccessToken);
        Assert.Equal(second.CollectorId, result.CollectorId);
        Assert.Equal(second.ApplicationId, result.ApplicationId);
    }

    [Fact]
    public void Authenticate_WithDuplicateTenantAndBusiness_ShouldThrow()
    {
        var first = CreateAccount();
        var second = CreateAccount();

        second.TenantId = first.TenantId;
        second.BusinessId = first.BusinessId;

        var authenticator = CreateAuthenticator(first, second);

        Assert.Throws<InvalidOperationException>(() =>
            authenticator.Authenticate(
                first.WebhookId,
                Sign(first.WebhookSecret),
                RequestId,
                DataId));
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("ts=1793217600,v1=invalid")]
    public void Authenticate_WithInvalidSignature_ShouldReturnNull(
        string? signature)
    {
        var account = CreateAccount();
        var authenticator = CreateAuthenticator(account);

        Assert.Null(authenticator.Authenticate(
            account.WebhookId,
            signature,
            RequestId,
            DataId));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("data")]
    public void Authenticate_WithChangedSignedValue_ShouldReturnNull(
        string changedValue)
    {
        var account = CreateAccount();
        var authenticator = CreateAuthenticator(account);

        Assert.Null(authenticator.Authenticate(
            account.WebhookId,
            Sign(account.WebhookSecret),
            changedValue == "request" ? "another-request" : RequestId,
            changedValue == "data" ? "ORDTST-OTHER" : DataId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Authenticate_WithMissingSecret_ShouldThrow(string? secret)
    {
        var account = CreateAccount();
        var signature = Sign(account.WebhookSecret);

        account.WebhookSecret = secret!;

        var authenticator = CreateAuthenticator(account);

        Assert.Throws<InvalidOperationException>(() =>
            authenticator.Authenticate(
                account.WebhookId,
                signature,
                RequestId,
                DataId));
    }

    [Theory]
    [InlineData("collector")]
    [InlineData("application")]
    [InlineData("token")]
    public void Authenticate_WithInvalidAccountConfiguration_ShouldThrow(
        string invalidField)
    {
        var account = CreateAccount();
        var signature = Sign(account.WebhookSecret);

        switch (invalidField)
        {
            case "collector":
                account.CollectorId = 0;
                break;

            case "application":
                account.ApplicationId = 0;
                break;

            case "token":
                account.AccessToken = "invalid token";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(invalidField));
        }

        var authenticator = CreateAuthenticator(account);

        Assert.Throws<InvalidOperationException>(() =>
            authenticator.Authenticate(
                account.WebhookId,
                signature,
                RequestId,
                DataId));
    }

    #endregion

    #region Helpers

    private static MercadoPagoAccountOptions CreateAccount()
    {
        return new MercadoPagoAccountOptions
        {
            TenantId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            AccessToken = "business-token",
            CollectorId = 987654321,
            ApplicationId = 123456789,
            WebhookId = Guid.NewGuid(),
            WebhookSecret = "webhook-test-secret"
        };
    }

    private static MercadoPagoWebhookAuthenticator CreateAuthenticator(
        params MercadoPagoAccountOptions[] accounts)
    {
        var options = new MercadoPagoOptions
        {
            WebhooksEnabled = true,
            Accounts = accounts.ToList()
        };

        return new MercadoPagoWebhookAuthenticator(
            options,
            new MercadoPagoAccountResolver(options));
    }

    private static string Sign(string secret)
    {
        var manifest =
            $"id:{DataId.ToLowerInvariant()};" +
            $"request-id:{RequestId};" +
            $"ts:{Timestamp};";

        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(manifest));

        return $"ts={Timestamp},v1={Convert.ToHexString(hash)}";
    }

    #endregion
}