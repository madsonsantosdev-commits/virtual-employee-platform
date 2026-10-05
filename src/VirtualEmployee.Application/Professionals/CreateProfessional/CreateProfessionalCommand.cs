namespace VirtualEmployee.Application.Professionals.CreateProfessional;

public sealed record CreateProfessionalCommand(
    Guid BusinessId,
    string Name);