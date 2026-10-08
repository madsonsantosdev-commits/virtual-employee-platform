namespace VirtualEmployee.Application.ProfessionalServices.ReplaceProfessionalServices;

public sealed record ReplaceProfessionalServicesCommand(
    Guid ProfessionalId,
    IReadOnlyList<Guid> ServiceIds);