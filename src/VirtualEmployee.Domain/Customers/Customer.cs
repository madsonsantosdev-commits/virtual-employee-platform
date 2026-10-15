using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Customers;

public sealed class Customer : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }

    public string? Name { get; private set; }
    public string Phone { get; private set; } = string.Empty;
    public string? Email { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Customer()
    {
    }

    public Customer(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string phone,
        DateTimeOffset createdAt,
        string? name = null,
        string? email = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Customer id cannot be empty.",
                nameof(id));
        }

        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException(
                "Tenant id cannot be empty.",
                nameof(tenantId));
        }

        if (businessId == Guid.Empty)
        {
            throw new ArgumentException(
                "Business id cannot be empty.",
                nameof(businessId));
        }

        ValidatePhone(phone);

        Id = id;
        TenantId = tenantId;
        BusinessId = businessId;
        Name = NormalizeOptional(name);
        Phone = phone.Trim();
        Email = NormalizeOptional(email);
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public void Update(
        string phone,
        DateTimeOffset updatedAt,
        string? name = null,
        string? email = null)
    {
        ValidatePhone(phone);

        var normalizedName = NormalizeOptional(name);
        var normalizedPhone = phone.Trim();
        var normalizedEmail = NormalizeOptional(email);

        if (Name == normalizedName &&
            Phone == normalizedPhone &&
            Email == normalizedEmail)
        {
            return;
        }

        Name = normalizedName;
        Phone = normalizedPhone;
        Email = normalizedEmail;
        UpdatedAt = updatedAt.ToUniversalTime();
    }

    private static void ValidatePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            throw new ArgumentException(
                "Customer phone cannot be empty.",
                nameof(phone));
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}