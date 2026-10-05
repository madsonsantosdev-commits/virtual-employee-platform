namespace VirtualEmployee.Application.Professionals;

public sealed record ProfessionalResponse(
    Guid Id,
    Guid BusinessId,
    string Name,
    bool IsActive);