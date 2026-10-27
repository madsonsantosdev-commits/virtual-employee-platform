using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualEmployee.Application.Payments.CreatePaymentAttempt;

namespace VirtualEmployee.Infrastructure.Payments;

internal static class PaymentAttemptRequestHasher
{
    public static string Compute(CreatePaymentAttemptCommand command)
    {
        var json = JsonSerializer.Serialize(new
        {
            command.AppointmentId,
            command.PaymentMethod
        });

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}