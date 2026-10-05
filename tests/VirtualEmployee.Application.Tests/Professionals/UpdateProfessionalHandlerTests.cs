using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Application.Professionals.UpdateProfessional;
using VirtualEmployee.Domain.Professionals;
using Xunit;

namespace VirtualEmployee.Application.Tests.Professionals;

public sealed class UpdateProfessionalHandlerTests
{
    [Fact]
    public async Task HandleAsync_ShouldUpdateProfessional_AndPreserveIdentity()
    {
        var professionalId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(
            2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

        var professional = new Professional(
            professionalId,
            tenantId,
            businessId,
            "Arthur Silva",
            createdAt);

        var writeService = new FakeProfessionalWriteService
        {
            ProfessionalToReturn = professional
        };

        var handler = new UpdateProfessionalHandler(writeService);
        var before = DateTimeOffset.UtcNow;

        var response = await handler.HandleAsync(
            new UpdateProfessionalCommand(
                professionalId,
                "  Arthur Santos  ",
                false));

        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(response);
        Assert.Equal(professionalId, writeService.RequestedProfessionalId);

        Assert.Equal(professionalId, professional.Id);
        Assert.Equal(tenantId, professional.TenantId);
        Assert.Equal(businessId, professional.BusinessId);
        Assert.Equal("Arthur Santos", professional.Name);
        Assert.False(professional.IsActive);
        Assert.Equal(createdAt, professional.CreatedAt);
        Assert.InRange(professional.UpdatedAt, before, after);

        Assert.Equal(professionalId, response.Id);
        Assert.Equal(businessId, response.BusinessId);
        Assert.Equal("Arthur Santos", response.Name);
        Assert.False(response.IsActive);
        Assert.Equal(0, writeService.SaveCalls);
    }

    [Fact]
    public async Task HandleAsync_ShouldReturnNull_WhenProfessionalDoesNotExist()
    {
        var professionalId = Guid.NewGuid();
        var writeService = new FakeProfessionalWriteService();
        var handler = new UpdateProfessionalHandler(writeService);

        var response = await handler.HandleAsync(
            new UpdateProfessionalCommand(
                professionalId,
                "Arthur Santos",
                false));

        Assert.Null(response);
        Assert.Equal(professionalId, writeService.RequestedProfessionalId);
        Assert.Equal(0, writeService.SaveCalls);
    }

    private sealed class FakeProfessionalWriteService
        : IProfessionalWriteService
    {
        public Professional? ProfessionalToReturn { get; init; }

        public Guid? RequestedProfessionalId { get; private set; }

        public int SaveCalls { get; private set; }

        public Task<bool> BusinessExistsAsync(
            Guid businessId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Professional?> GetByIdAsync(
            Guid professionalId,
            CancellationToken cancellationToken = default)
        {
            RequestedProfessionalId = professionalId;

            return Task.FromResult(ProfessionalToReturn);
        }

        public Task AddAsync(
            Professional professional,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            SaveCalls++;

            return Task.CompletedTask;
        }
    }
}