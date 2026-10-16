using System.Security.Cryptography;
using System.Text.Json;
using VirtualEmployee.Application.Appointments.CreateAppointment;

namespace VirtualEmployee.Infrastructure.Appointments;

public static class AppointmentRequestHasher
{
    public static string Compute(CreateAppointmentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            command.CustomerId,
            command.LocationId,
            command.ProfessionalId,
            ServiceIds = command.ServiceIds.ToArray(),
            StartsAtUtcTicks = command.StartsAt.UtcDateTime.Ticks
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }
}