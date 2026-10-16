using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Scheduling;

public sealed class AppointmentItemTests
{
    #region Facts

    [Fact]
    public void Constructor_ShouldPreserveIdentityAndCommercialSnapshots()
    {
        var id = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var appointmentId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var item = new AppointmentItem(
            id,
            tenantId,
            appointmentId,
            serviceId,
            " Corte masculino ",
            65m,
            45);

        Assert.Equal(id, item.Id);
        Assert.Equal(tenantId, item.TenantId);
        Assert.Equal(appointmentId, item.AppointmentId);
        Assert.Equal(serviceId, item.ServiceId);
        Assert.Equal("Corte masculino", item.ServiceNameSnapshot);
        Assert.Equal(65m, item.PriceSnapshot);
        Assert.Equal(45, item.DurationMinutesSnapshot);
    }

    [Fact]
    public void Constructor_WithZeroPrice_ShouldAccept()
    {
        var item = CreateItem(price: 0m);

        Assert.Equal(0m, item.PriceSnapshot);
        Assert.Equal(45, item.DurationMinutesSnapshot);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("id")]
    [InlineData("tenantId")]
    [InlineData("appointmentId")]
    [InlineData("serviceId")]
    public void Constructor_WithEmptyIdentity_ShouldReject(
        string parameter)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new AppointmentItem(
                parameter == "id" ? Guid.Empty : Guid.NewGuid(),
                parameter == "tenantId" ? Guid.Empty : Guid.NewGuid(),
                parameter == "appointmentId" ? Guid.Empty : Guid.NewGuid(),
                parameter == "serviceId" ? Guid.Empty : Guid.NewGuid(),
                "Corte",
                65m,
                45));

        Assert.Equal(parameter, exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithBlankServiceName_ShouldReject(
        string? name)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => CreateItem(name: name!));

        Assert.Equal("serviceNameSnapshot", exception.ParamName);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_WithNegativePrice_ShouldReject(int price)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateItem(price: price));

        Assert.Equal("priceSnapshot", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_WithInvalidDuration_ShouldReject(int duration)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateItem(duration: duration));

        Assert.Equal("durationMinutesSnapshot", exception.ParamName);
    }

    #endregion

    #region Helpers

    private static AppointmentItem CreateItem(
        string name = "Corte",
        decimal price = 65m,
        int duration = 45)
    {
        return new AppointmentItem(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            name,
            price,
            duration);
    }

    #endregion
}