using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Domain.Professionals;

namespace VirtualEmployee.Application.Professionals.CreateProfessional;

public sealed class CreateProfessionalHandler
{
    private readonly ITenantContext _tenantContext;
    private readonly IProfessionalWriteService _professionalWriteService;

    public CreateProfessionalHandler(
        ITenantContext tenantContext,
        IProfessionalWriteService professionalWriteService)
    {
        _tenantContext = tenantContext;
        _professionalWriteService = professionalWriteService;
    }

    public async Task<ProfessionalResponse?> HandleAsync(
        CreateProfessionalCommand command,
        CancellationToken cancellationToken = default)
    {
        var businessExists =
            await _professionalWriteService.BusinessExistsAsync(
                command.BusinessId,
                cancellationToken);

        if (!businessExists)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        var professional = new Professional(
            Guid.NewGuid(),
            _tenantContext.TenantId,
            command.BusinessId,
            command.Name,
            now);

        await _professionalWriteService.AddAsync(
            professional,
            cancellationToken);

        return new ProfessionalResponse(
            professional.Id,
            professional.BusinessId,
            professional.Name,
            professional.IsActive);
    }
}