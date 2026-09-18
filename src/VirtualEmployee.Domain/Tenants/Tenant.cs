namespace VirtualEmployee.Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    private Tenant()
    {
    }

    public Tenant(Guid id, string name)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tenant name cannot be empty.", nameof(name));
        }

        Id = id;
        Name = name.Trim();
    }
}