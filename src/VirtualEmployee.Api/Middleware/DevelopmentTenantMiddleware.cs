using VirtualEmployee.Application.Common.Tenancy;

namespace VirtualEmployee.Api.Middleware;

public sealed class DevelopmentTenantMiddleware
{
    private const string TenantHeaderName = "X-Tenant-Id";

    private readonly RequestDelegate _next;

    public DevelopmentTenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContextInitializer tenantContextInitializer)
    {
        // Endpoints that don't access tenant-scoped application data
        // must remain accessible without a tenant context.
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(
                TenantHeaderName,
                out var tenantHeader))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            await context.Response.WriteAsJsonAsync(new
            {
                code = "TENANT_HEADER_REQUIRED",
                detail = $"Header '{TenantHeaderName}' is required in Development."
            });

            return;
        }

        if (!Guid.TryParse(tenantHeader.ToString(), out var tenantId) ||
            tenantId == Guid.Empty)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;

            await context.Response.WriteAsJsonAsync(new
            {
                code = "INVALID_TENANT_ID",
                detail = $"Header '{TenantHeaderName}' must contain a valid tenant id."
            });

            return;
        }

        tenantContextInitializer.Initialize(tenantId);

        await _next(context);
    }
}