using System.Security.Cryptography;
using System.Text;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public static class MercadoPagoWebhookSignatureValidator
{
    public static bool Validate(
        string secret,
        string? signature,
        string? requestId,
        string? dataId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        if (string.IsNullOrWhiteSpace(signature) ||
            signature.Length > 1024 ||
            !IsValidIdentifier(requestId) ||
            !IsValidIdentifier(dataId))
        {
            return false;
        }

        string? timestamp = null;
        string? receivedHash = null;

        foreach (var part in signature.Split(','))
        {
            var separator = part.IndexOf('=');

            if (separator <= 0)
            {
                return false;
            }

            var name = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();

            if (name == "ts")
            {
                if (timestamp is not null)
                {
                    return false;
                }

                timestamp = value;
            }
            else if (name == "v1")
            {
                if (receivedHash is not null)
                {
                    return false;
                }

                receivedHash = value;
            }
            else
            {
                return false;
            }
        }

        if (string.IsNullOrEmpty(timestamp) ||
            timestamp.Length > 20 ||
            timestamp.Any(character =>
                character < '0' || character > '9') ||
            string.IsNullOrEmpty(receivedHash) ||
            receivedHash.Length != 64)
        {
            return false;
        }

        byte[] receivedBytes;

        try
        {
            receivedBytes = Convert.FromHexString(receivedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var manifest =
            $"id:{dataId!.ToLowerInvariant()};" +
            $"request-id:{requestId};" +
            $"ts:{timestamp};";

        var expectedBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(manifest));

        return CryptographicOperations.FixedTimeEquals(
            expectedBytes,
            receivedBytes);
    }

    private static bool IsValidIdentifier(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            value.Length <= 200 &&
            value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character == '-');
    }
}