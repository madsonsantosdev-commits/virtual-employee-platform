namespace VirtualEmployee.Application.ProfessionalLocations.ReplaceProfessionalLocations;

public sealed record ReplaceProfessionalLocationsCommand(
    Guid ProfessionalId,
    IReadOnlyList<Guid> LocationIds);