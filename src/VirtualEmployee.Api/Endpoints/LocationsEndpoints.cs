using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Application.Locations.GetLocations;

namespace VirtualEmployee.Api.Endpoints;

public static class LocationsEndpoints
{
    public static IEndpointRouteBuilder MapLocationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/locations")
            .WithTags("Locations");

        group.MapGet(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetLocationsHandler handler,
                CancellationToken cancellationToken) =>
            {
                var locations = await handler.HandleAsync(
                    cancellationToken);

                return Results.Ok(locations);
            })
            .WithName("GetLocations")
            .WithSummary("Lista as unidades do tenant atual")
            .WithDescription(
                "Retorna as unidades pertencentes ao tenant atual. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<IReadOnlyList<LocationResponse>>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest);

        return endpoints;
    }
}