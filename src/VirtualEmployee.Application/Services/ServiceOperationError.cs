namespace VirtualEmployee.Application.Services;

public enum ServiceOperationError
{
    None = 0,
    BusinessNotFound,
    ServiceNotFound,
    InvalidCombo,
    ComboComponentInvalid,
    ComboNestingNotAllowed
}