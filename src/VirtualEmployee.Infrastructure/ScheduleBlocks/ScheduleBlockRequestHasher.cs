using System.Security.Cryptography;
using System.Text.Json;
using VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;

namespace VirtualEmployee.Infrastructure.ScheduleBlocks;

public static class ScheduleBlockRequestHasher
{
    public static string Compute(CreateScheduleBlockCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            command.LocationId,
            command.ProfessionalId,
            StartsAtUtcTicks = command.StartsAt.UtcDateTime.Ticks,
            EndsAtUtcTicks = command.EndsAt.UtcDateTime.Ticks
        });

        return Convert.ToHexString(SHA256.HashData(payload));
    }
}