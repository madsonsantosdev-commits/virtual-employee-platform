using VirtualEmployee.Application.Customers;
using Xunit;

namespace VirtualEmployee.Application.Tests.Customers;

public sealed class CustomerPhoneNormalizerTests
{
    #region Facts

    [Fact]
    public void TryNormalize_ShouldBeIdempotent()
    {
        Assert.True(
            CustomerPhoneNormalizer.TryNormalize(
                "+55 (11) 99999-0001",
                out var first));

        Assert.True(
            CustomerPhoneNormalizer.TryNormalize(
                first,
                out var second));

        Assert.Equal(first, second);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("+5511999990001", "+5511999990001")]
    [InlineData("  +55 (11) 99999-0001  ", "+5511999990001")]
    [InlineData("+55.11.99999.0001", "+5511999990001")]
    [InlineData("+1 (212) 555-0123", "+12125550123")]
    [InlineData("+123456789012345", "+123456789012345")]
    public void TryNormalize_WithSupportedFormat_ShouldReturnCanonicalPhone(
        string input,
        string expected)
    {
        var success = CustomerPhoneNormalizer.TryNormalize(
            input,
            out var normalized);

        Assert.True(success);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("5511999990001")]
    [InlineData("005511999990001")]
    [InlineData("+")]
    [InlineData("+ ()-.")]
    [InlineData("+05511999990001")]
    [InlineData("+55+11999990001")]
    [InlineData("+55 (11) ABCDE-0001")]
    [InlineData("+5511999990001 ramal 12")]
    [InlineData("+1234567890123456")]
    [InlineData("+55\t11999990001")]
    [InlineData("+５５１１９９９９９０００１")]
    public void TryNormalize_WithInvalidInput_ShouldReturnFalse(
        string? input)
    {
        var success = CustomerPhoneNormalizer.TryNormalize(
            input,
            out var normalized);

        Assert.False(success);
        Assert.Equal(string.Empty, normalized);
    }

    #endregion
}