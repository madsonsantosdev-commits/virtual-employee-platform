using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Professionals;

public sealed class ProfessionalLocation : ITenantScoped
{
    private ProfessionalLocation()
    {
    }

    public ProfessionalLocation(
        Guid tenantId,
        Guid professionalId,
        Guid locationId,
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

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException(
                "LocationId cannot be empty.",
                nameof(locationId));
        }

        TenantId = tenantId;
        ProfessionalId = professionalId;
        LocationId = locationId;
        IsActive = true;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid TenantId { get; private set; }

    public Guid ProfessionalId { get; private set; }

    public Guid LocationId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        bool isActive,
        DateTimeOffset updatedAt)
    {
        IsActive = isActive;
        UpdatedAt = updatedAt;
    }
}