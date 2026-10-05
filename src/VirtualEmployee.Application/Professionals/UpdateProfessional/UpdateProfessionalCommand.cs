namespace VirtualEmployee.Application.Professionals.UpdateProfessional;

public sealed record UpdateProfessionalCommand(
    Guid ProfessionalId,
    string Name,
    bool IsActive);