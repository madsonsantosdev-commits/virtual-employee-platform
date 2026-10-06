namespace VirtualEmployee.Application.ProfessionalLocations;

public sealed record ProfessionalLocationOperationResult(
    ProfessionalLocationOperationError Error)
{
    public bool IsSuccess =>
        Error == ProfessionalLocationOperationError.None;

    public static ProfessionalLocationOperationResult Success()
    {
        return new ProfessionalLocationOperationResult(
            ProfessionalLocationOperationError.None);
    }

    public static ProfessionalLocationOperationResult Failure(
        ProfessionalLocationOperationError error)
    {
        return new ProfessionalLocationOperationResult(error);
    }
}