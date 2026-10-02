using System.Text.Json;

namespace VirtualEmployee.Api.Serialization;

public sealed class UpperCaseJsonNamingPolicy : JsonNamingPolicy
{
    public override string ConvertName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name.ToUpperInvariant();
    }
}