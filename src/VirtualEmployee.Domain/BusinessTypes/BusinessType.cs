namespace VirtualEmployee.Domain.BusinessTypes;

public sealed class BusinessType
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private BusinessType()
    {
    }

    public BusinessType(
        Guid id,
        string code,
        string name,
        bool isSystem,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Business type id cannot be empty.",
                nameof(id));
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException(
                "Business type code cannot be empty.",
                nameof(code));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "Business type name cannot be empty.",
                nameof(name));
        }

        Id = id;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsSystem = isSystem;
        IsActive = true;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }
}