using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Application.Professionals.CreateProfessional;
using VirtualEmployee.Domain.Professionals;
using Xunit;

namespace VirtualEmployee.Application.Tests.Professionals;

public sealed class CreateProfessionalHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldCreateProfessional_WhenBusinessExists()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();

        var writeService = new FakeProfessionalWriteService
        {
            BusinessExists = true
        };

        var handler = new CreateProfessionalHandler(
            new FakeTenantContext(tenantId),
            writeService);

        var before = DateTimeOffset.UtcNow;

        var response = await handler.HandleAsync(
            new CreateProfessionalCommand(
                businessId,
                "  Arthur Silva  "));

        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(response);

        var professional = writeService.AddedProfessional;
        Assert.NotNull(professional);

        Assert.NotEqual(Guid.Empty, professional.Id);
        Assert.Equal(tenantId, professional.TenantId);
        Assert.Equal(businessId, professional.BusinessId);
        Assert.Equal("Arthur Silva", professional.Name);
        Assert.True(professional.IsActive);
        Assert.InRange(professional.CreatedAt, before, after);
        Assert.Equal(professional.CreatedAt, professional.UpdatedAt);

        Assert.Equal(professional.Id, response.Id);
        Assert.Equal(businessId, response.BusinessId);
        Assert.Equal("Arthur Silva", response.Name);
        Assert.True(response.IsActive);

        Assert.Equal(businessId, writeService.CheckedBusinessId);
        Assert.Equal(1, writeService.AddCalls);
        Assert.Equal(0, writeService.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNull_WhenBusinessDoesNotExist()
    {
        var businessId = Guid.NewGuid();

        var writeService = new FakeProfessionalWriteService
        {
            BusinessExists = false
        };

        var handler = new CreateProfessionalHandler(
            new FakeTenantContext(Guid.NewGuid()),
            writeService);

        var response = await handler.HandleAsync(
            new CreateProfessionalCommand(
                businessId,
                "Arthur Silva"));

        Assert.Null(response);
        Assert.Equal(businessId, writeService.CheckedBusinessId);
        Assert.Null(writeService.AddedProfessional);
        Assert.Equal(0, writeService.AddCalls);
        Assert.Equal(0, writeService.SaveCalls);
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        public FakeTenantContext(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public Guid TenantId { get; }

        public bool IsInitialized => true;
    }

    private sealed class FakeProfessionalWriteService
        : IProfessionalWriteService
    {
        public bool BusinessExists { get; init; }

        public Guid? CheckedBusinessId { get; private set; }

        public Professional? AddedProfessional { get; private set; }

        public int AddCalls { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<bool> BusinessExistsAsync(
            Guid businessId,
            CancellationToken cancellationToken = default)
        {
            CheckedBusinessId = businessId;

            return Task.FromResult(BusinessExists);
        }

        public Task<Professional?> GetByIdAsync(
            Guid professionalId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<Professional?>(null);
        }

        public Task AddAsync(
            Professional professional,
            CancellationToken cancellationToken = default)
        {
            AddedProfessional = professional;
            AddCalls++;

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;

            return Task.CompletedTask;
        }
    }
}