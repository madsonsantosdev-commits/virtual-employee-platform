namespace VirtualEmployee.Domain.Payments;

public enum PaymentAttemptStatus
{
    Created = 1,
    Pending = 2,
    Processing = 3,
    Confirmed = 4,
    Failed = 5,
    Expired = 6,
    Cancelled = 7
}