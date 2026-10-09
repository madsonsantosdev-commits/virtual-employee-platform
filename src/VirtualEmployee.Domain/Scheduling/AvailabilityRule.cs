using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Scheduling;

public sealed class AvailabilityRule : ITenantScoped
{
    private AvailabilityRule()
    {
    }

    public AvailabilityRule(
        Guid id,
        Guid tenantId,
        Guid locationId,
        Guid professionalId,
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Id cannot be empty.",
                nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "TenantId cannot be empty.",
                nameof(tenantId));
        }

        if (locationId == Guid.Empty)
        {
            throw new ArgumentException(
                "LocationId cannot be empty.",
                nameof(locationId));
        }

        if (professionalId == Guid.Empty)
        {
            throw new ArgumentException(
                "ProfessionalId cannot be empty.",
                nameof(professionalId));
        }

        ValidateWindow(dayOfWeek, startTime, endTime);

        Id = id;
        TenantId = tenantId;
        LocationId = locationId;
        ProfessionalId = professionalId;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        IsActive = true;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid ProfessionalId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly StartTime { get; private set; }

    public TimeOnly EndTime { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Update(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        bool isActive,
        DateTimeOffset updatedAt)
    {
        ValidateWindow(dayOfWeek, startTime, endTime);

        if (DayOfWeek == dayOfWeek &&
            StartTime == startTime &&
            EndTime == endTime &&
            IsActive == isActive)
        {
            return;
        }

        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        IsActive = isActive;
        UpdatedAt = updatedAt;
    }

    private static void ValidateWindow(
        DayOfWeek dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime)
    {
        if (!Enum.IsDefined(dayOfWeek))
        {
            throw new ArgumentOutOfRangeException(
                nameof(dayOfWeek),
                "DayOfWeek must be between 0 and 6.");
        }

        if (startTime >= endTime)
        {
            throw new ArgumentException(
                "StartTime must be earlier than EndTime.",
                nameof(endTime));
        }
    }
}