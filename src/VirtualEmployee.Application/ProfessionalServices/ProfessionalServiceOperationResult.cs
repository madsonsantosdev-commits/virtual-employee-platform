namespace VirtualEmployee.Application.ProfessionalServices;

public sealed record ProfessionalServiceOperationResult(
    ProfessionalServiceOperationError Error)
{
    public bool IsSuccess =>
        Error == ProfessionalServiceOperationError.None;

    public static ProfessionalServiceOperationResult Success()
    {
        return new ProfessionalServiceOperationResult(
            ProfessionalServiceOperationError.None);
    }

    public static ProfessionalServiceOperationResult Failure(
        ProfessionalServiceOperationError error)
    {
        return new ProfessionalServiceOperationResult(error);
    }
}