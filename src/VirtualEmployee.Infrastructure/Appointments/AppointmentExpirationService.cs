using Microsoft.EntityFrameworkCore;
using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;

namespace VirtualEmployee.Infrastructure.Appointments;

public sealed class AppointmentExpirationService
    : IAppointmentExpirationService
{
    private readonly AppDbContext _dbContext;

    public AppointmentExpirationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> ExpireDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var utc = now.ToUniversalTime();

        var updatedAt = new DateTimeOffset(
            utc.Ticks - utc.Ticks % 10,
            TimeSpan.Zero);

        var tenantId = _dbContext.CurrentTenantId;
        var pendingStatus = (int)AppointmentStatus.Pending;
        var expiredStatus = (int)AppointmentStatus.Expired;
        var historyType = (int)AppointmentHistoryType.Expired;

        // SQL direto exige filtro explícito de tenant.
        // O snapshot é capturado com o registro bloqueado.
        // UPDATE e INSERT fazem parte da mesma instrução atômica.
        return _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH candidates AS MATERIALIZED (
                SELECT
                    a.id,
                    a.tenant_id,
                    jsonb_build_object(
                        'Status', {pendingStatus},
                        'LocationId', a.location_id,
                        'ProfessionalId', a.professional_id,
                        'StartsAt',
                            to_char(
                                a.starts_at AT TIME ZONE 'UTC',
                                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                        'EndsAt',
                            to_char(
                                a.ends_at AT TIME ZONE 'UTC',
                                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"'),
                        'ReservationExpiresAt',
                            to_char(
                                a.reservation_expires_at AT TIME ZONE 'UTC',
                                'YYYY-MM-DD"T"HH24:MI:SS.US"Z"')
                    ) AS before_state
                FROM appointments AS a
                WHERE a.tenant_id = {tenantId}
                  AND a.status = 'PENDING'
                  AND a.reservation_expires_at IS NOT NULL
                  AND a.reservation_expires_at <= {updatedAt}
                ORDER BY a.id
                FOR UPDATE OF a
            ),
            expired AS (
                UPDATE appointments AS a
                SET
                    status = 'EXPIRED',
                    updated_at = {updatedAt}
                FROM candidates AS c
                WHERE a.id = c.id
                  AND a.tenant_id = c.tenant_id
                  AND a.tenant_id = {tenantId}
                  AND a.status = 'PENDING'
                  AND a.reservation_expires_at <= {updatedAt}
                RETURNING
                    a.id,
                    a.tenant_id,
                    c.before_state
            )
            INSERT INTO appointment_history_entries (
                id,
                tenant_id,
                appointment_id,
                type,
                occurred_at,
                before_state,
                after_state,
                reason
            )
            SELECT
                gen_random_uuid(),
                e.tenant_id,
                e.id,
                {historyType},
                {updatedAt},
                e.before_state,
                jsonb_set(
                    e.before_state,
                    ARRAY['Status'],
                    to_jsonb({expiredStatus}::integer)),
                NULL
            FROM expired AS e;
            """,
            cancellationToken);
    }
}