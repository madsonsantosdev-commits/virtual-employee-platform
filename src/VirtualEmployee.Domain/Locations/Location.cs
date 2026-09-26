using VirtualEmployee.Domain.Common;

namespace VirtualEmployee.Domain.Locations;

public sealed class Location : ITenantScoped
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BusinessId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public string? Address { get; private set; }

    public string CountryCode { get; private set; } = string.Empty;
    public string Timezone { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Location()
    {
    }

    public Location(
        Guid id,
        Guid tenantId,
        Guid businessId,
        string name,
        string countryCode,
        string timezone,
        DateTimeOffset createdAt,
        string? phone = null,
        string? address = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Location id cannot be empty.",
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

        ValidateRequiredFields(
            name,
            countryCode,
            timezone);

        Id = id;
        TenantId = tenantId;
        BusinessId = businessId;

        ApplyOperationalData(
            name,
            countryCode,
            timezone,
            phone,
            address);

        IsActive = true;

        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public void Update(
        string name,
        string countryCode,
        string timezone,
        bool isActive,
        DateTimeOffset updatedAt,
        string? phone = null,
        string? address = null)
    {
        ValidateRequiredFields(
            name,
            countryCode,
            timezone);

        ApplyOperationalData(
            name,
            countryCode,
            timezone,
            phone,
            address);

        IsActive = isActive;
        UpdatedAt = updatedAt;
    }

    private void ApplyOperationalData(
        string name,
        string countryCode,
        string timezone,
        string? phone,
        string? address)
    {
        Name = name.Trim();
        Phone = NormalizeOptional(phone);
        Address = NormalizeOptional(address);

        CountryCode = countryCode.Trim().ToUpperInvariant();
        Timezone = timezone.Trim();
    }

    private static void ValidateRequiredFields(
        string name,
        string countryCode,
        string timezone)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Location name cannot be empty.",
                nameof(name));
        }

        if (string.IsNullOrWhiteSpace(countryCode))
        {
            throw new ArgumentException(
                "Country code cannot be empty.",
                nameof(countryCode));
        }

        if (string.IsNullOrWhiteSpace(timezone))
        {
            throw new ArgumentException(
                "Timezone cannot be empty.",
                nameof(timezone));
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}