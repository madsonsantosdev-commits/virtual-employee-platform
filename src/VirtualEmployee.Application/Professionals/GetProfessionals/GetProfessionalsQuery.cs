namespace VirtualEmployee.Application.Professionals.GetProfessionals;

public sealed record GetProfessionalsQuery(
    bool? Active = null,
    Guid? LocationId = null,
    IReadOnlyList<Guid>? ServiceIds = null);