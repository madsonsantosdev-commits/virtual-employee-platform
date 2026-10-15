using System.Text;

namespace VirtualEmployee.Application.Customers;

public static class CustomerPhoneNormalizer
{
    public static bool TryNormalize(
        string? phone,
        out string normalizedPhone)
    {
        normalizedPhone = string.Empty;

        if (string.IsNullOrWhiteSpace(phone))
        {
            return false;
        }

        var input = phone.Trim();

        if (!input.StartsWith('+'))
        {
            return false;
        }

        var digits = new StringBuilder();

        foreach (var character in input.AsSpan(1))
        {
            if (character is >= '0' and <= '9')
            {
                digits.Append(character);

                if (digits.Length > 15)
                {
                    return false;
                }

                continue;
            }

            if (character is ' ' or '(' or ')' or '-' or '.')
            {
                continue;
            }

            return false;
        }

        if (digits.Length == 0 || digits[0] == '0')
        {
            return false;
        }

        normalizedPhone = "+" + digits;
        return true;
    }
}