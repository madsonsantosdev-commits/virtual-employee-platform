using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Scheduling;

public sealed class ScheduleBlock : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid LocationId { get; private set; }
    public Guid? ProfessionalId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }
    public DateTimeOffset EndsAt { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ScheduleBlock()
    {
    }

    public ScheduleBlock(
        Guid id,
        Guid tenantId,
        Guid locationId,
        Guid? professionalId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Schedule block id cannot be empty.",
                nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException(
                "Location id cannot be empty.",
                nameof(locationId));
        }

        if (professionalId == Guid.Empty)
        {
            throw new ArgumentException(
                "Professional id must be null or non-empty.",
                nameof(professionalId));
        }

        ValidateInterval(startsAt, endsAt);

        Id = id;
        TenantId = tenantId;
        LocationId = locationId;
        ProfessionalId = professionalId;
        StartsAt = startsAt.ToUniversalTime();
        EndsAt = endsAt.ToUniversalTime();
        IsActive = true;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public void Deactivate(DateTimeOffset updatedAt)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    public bool Overlaps(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        ValidateInterval(startsAt, endsAt);

        return IsActive &&
            StartsAt < endsAt &&
            startsAt < EndsAt;
    }

    private static void ValidateInterval(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt)
    {
        if (startsAt >= endsAt)
        {
            throw new ArgumentException(
                "Start must be earlier than end.",
                nameof(endsAt));
        }
    }
}