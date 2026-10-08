using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Professionals;

public sealed class ProfessionalService : ITenantScoped
{
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ProfessionalService()
    {
    }

    public ProfessionalService(
        Guid tenantId,
        Guid professionalId,
        Guid serviceId,
        DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "TenantId cannot be empty.",
                nameof(tenantId));
        }

        if (professionalId == Guid.Empty)
        {
            throw new ArgumentException(
                "ProfessionalId cannot be empty.",
                nameof(professionalId));
        }

        if (serviceId == Guid.Empty)
        {
            throw new ArgumentException(
                "ServiceId cannot be empty.",
                nameof(serviceId));
        }

        TenantId = tenantId;
        ProfessionalId = professionalId;
        ServiceId = serviceId;

        IsActive = true;

        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public void Update(
        bool isActive,
        DateTimeOffset updatedAt)
    {
        IsActive = isActive;
        UpdatedAt = updatedAt;
    }
}