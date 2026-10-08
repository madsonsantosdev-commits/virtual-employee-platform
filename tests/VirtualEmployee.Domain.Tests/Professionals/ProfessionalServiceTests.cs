using VirtualEmployee.Domain.Professionals;
using Xunit;

namespace VirtualEmployee.Domain.Tests.Professionals;

public sealed class ProfessionalServiceTests
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Constructor_WithValidData_ShouldCreateActiveLink()
    {
        var tenantId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var link = new ProfessionalService(
            tenantId,
            professionalId,
            serviceId,
            CreatedAt);

        Assert.Equal(tenantId, link.TenantId);
        Assert.Equal(professionalId, link.ProfessionalId);
        Assert.Equal(serviceId, link.ServiceId);
        Assert.True(link.IsActive);
        Assert.Equal(CreatedAt, link.CreatedAt);
        Assert.Equal(CreatedAt, link.UpdatedAt);
    }

    [Theory]
    [InlineData("tenantId")]
    [InlineData("professionalId")]
    [InlineData("serviceId")]
    public void Constructor_WithEmptyId_ShouldThrow(
        string parameterName)
    {
        var tenantId = parameterName == "tenantId"
            ? Guid.Empty
            : Guid.NewGuid();

        var professionalId = parameterName == "professionalId"
            ? Guid.Empty
            : Guid.NewGuid();

        var serviceId = parameterName == "serviceId"
            ? Guid.Empty
            : Guid.NewGuid();

        var exception = Assert.Throws<ArgumentException>(() =>
            new ProfessionalService(
                tenantId,
                professionalId,
                serviceId,
                CreatedAt));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Update_ShouldDeactivateAndReactivateWithoutChangingIdentityOrCreatedAt()
    {
        var tenantId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();

        var link = new ProfessionalService(
            tenantId,
            professionalId,
            serviceId,
            CreatedAt);

        var deactivatedAt = CreatedAt.AddHours(1);

        link.Update(false, deactivatedAt);

        Assert.False(link.IsActive);
        Assert.Equal(deactivatedAt, link.UpdatedAt);
        Assert.Equal(CreatedAt, link.CreatedAt);

        var reactivatedAt = CreatedAt.AddHours(2);

        link.Update(true, reactivatedAt);

        Assert.True(link.IsActive);
        Assert.Equal(reactivatedAt, link.UpdatedAt);
        Assert.Equal(CreatedAt, link.CreatedAt);
        Assert.Equal(tenantId, link.TenantId);
        Assert.Equal(professionalId, link.ProfessionalId);
        Assert.Equal(serviceId, link.ServiceId);
    }
}