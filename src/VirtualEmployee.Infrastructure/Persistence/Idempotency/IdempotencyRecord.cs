using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Infrastructure.Persistence.Idempotency;

public sealed class IdempotencyRecord : ITenantScoped
{
    public Guid TenantId { get; private set; }
    public string Operation { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public string ResponseJson { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    private IdempotencyRecord()
    {
    }

    public IdempotencyRecord(
        Guid tenantId,
        string operation,
        string key,
        string requestHash,
        string responseJson,
        DateTimeOffset createdAt)
    {
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(responseJson);

        TenantId = tenantId;
        Operation = operation;
        Key = key;
        RequestHash = requestHash;
        ResponseJson = responseJson;
        CreatedAt = createdAt.ToUniversalTime();
    }
}