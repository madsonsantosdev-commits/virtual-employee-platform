using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Application.Locations.CreateLocation;
using VirtualEmployee.Application.Locations.GetLocations;
using VirtualEmployee.Application.Locations.UpdateLocation;
using VirtualEmployee.Application.LocationServices;
using VirtualEmployee.Application.LocationServices.GetLocationServices;
using VirtualEmployee.Application.LocationServices.ReplaceLocationServices;
using VirtualEmployee.Application.Services;

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

        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetLocationByIdHandler handler,
                CancellationToken cancellationToken) =>
            {
                var location = await handler.HandleAsync(
                    id,
                    cancellationToken);

                return location is null
                    ? Results.NotFound()
                    : Results.Ok(location);
            })
            .WithName("GetLocationById")
            .WithSummary("Obtém uma unidade por identificador")
            .WithDescription(
                "Retorna a unidade pertencente ao tenant atual. " +
                "Unidades inexistentes ou pertencentes a outro tenant retornam 404. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<LocationResponse>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status404NotFound);

        group.MapPost(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                CreateLocationRequest request,
                CreateLocationHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.BusinessId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(request.Name) ||
                    string.IsNullOrWhiteSpace(request.CountryCode) ||
                    string.IsNullOrWhiteSpace(request.Timezone))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid location request",
                        detail:
                            "BusinessId, name, countryCode and timezone are required.");
                }

                var command = new CreateLocationCommand(
                    request.BusinessId,
                    request.Name,
                    request.CountryCode,
                    request.Timezone,
                    request.Phone,
                    request.Address);

                var location = await handler.HandleAsync(
                    command,
                    cancellationToken);

                return location is null
                    ? Results.NotFound()
                    : Results.Created(
                        $"/api/v1/locations/{location.Id}",
                        location);
            })
            .WithName("CreateLocation")
            .WithSummary("Cria uma unidade")
            .WithDescription(
                "Cria uma unidade para um Business pertencente ao tenant atual. " +
                "Business inexistente ou pertencente a outro tenant retorna 404. " +
                "O TenantId é obtido do contexto da requisição e não do payload. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<LocationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut(
            "/{id:guid}",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                UpdateLocationRequest request,
                UpdateLocationHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) ||
                    string.IsNullOrWhiteSpace(request.CountryCode) ||
                    string.IsNullOrWhiteSpace(request.Timezone))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid location request",
                        detail:
                            "Name, countryCode and timezone are required.");
                }

                var command = new UpdateLocationCommand(
                    id,
                    request.Name,
                    request.CountryCode,
                    request.Timezone,
                    request.IsActive,
                    request.Phone,
                    request.Address);

                var location = await handler.HandleAsync(
                    command,
                    cancellationToken);

                return location is null
                    ? Results.NotFound()
                    : Results.Ok(location);
            })
            .WithName("UpdateLocation")
            .WithSummary("Atualiza uma unidade")
            .WithDescription(
                "Atualiza os dados operacionais de uma unidade pertencente ao tenant atual. " +
                "Location inexistente ou pertencente a outro tenant retorna 404. " +
                "TenantId e BusinessId não podem ser alterados. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<LocationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
                group.MapGet(
            "/{id:guid}/services",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetLocationServicesHandler handler,
                CancellationToken cancellationToken) =>
            {
                var services = await handler.HandleAsync(
                    id,
                    cancellationToken);

                return services is null
                    ? Results.NotFound()
                    : Results.Ok(services);
            })
            .WithName("GetLocationServices")
            .WithSummary("Lista os serviços oferecidos por uma unidade")
            .WithDescription(
                "Retorna os serviços vinculados à unidade pertencente ao tenant atual. " +
                "Uma unidade válida sem serviços retorna uma lista vazia. " +
                "Unidade inexistente ou pertencente a outro tenant retorna 404. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<IReadOnlyList<ServiceResponse>>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status404NotFound);

        group.MapPut(
            "/{id:guid}/services",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                ReplaceLocationServicesRequest request,
                ReplaceLocationServicesHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.ServiceIds is null)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid location services request",
                        detail: "ServiceIds is required.");
                }

                var command = new ReplaceLocationServicesCommand(
                    id,
                    request.ServiceIds);

                var result = await handler.HandleAsync(
                    command,
                    cancellationToken);

                return result.Error switch
                {
                    LocationServiceOperationError.None =>
                        Results.Ok(),

                    LocationServiceOperationError.LocationNotFound =>
                        Results.NotFound(),

                    LocationServiceOperationError.ServiceNotFound =>
                        Results.NotFound(),

                    LocationServiceOperationError.DuplicateService =>
                        Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Invalid location services request",
                            detail: "ServiceIds must not contain duplicates."),

                    LocationServiceOperationError.ServiceFromAnotherBusiness =>
                        Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Service does not belong to the location business",
                            detail:
                                "All services must belong to the same Business as the Location."),

                    _ =>
                        Results.Problem(
                            statusCode: StatusCodes.Status500InternalServerError,
                            title: "Unexpected location services error")
                };
            })
            .WithName("ReplaceLocationServices")
            .WithSummary("Substitui os serviços oferecidos por uma unidade")
            .WithDescription(
                "Substitui integralmente os serviços vinculados à unidade. " +
                "Uma lista vazia remove todos os vínculos. " +
                "Serviços duplicados retornam 400. " +
                "Location ou Service inexistente ou pertencente a outro tenant retorna 404. " +
                "Service pertencente a outro Business do mesmo tenant retorna 409. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
        
        return endpoints;
    }
}

public sealed record CreateLocationRequest(
    Guid BusinessId,
    string Name,
    string CountryCode,
    string Timezone,
    string? Phone,
    string? Address);

public sealed record UpdateLocationRequest(
    string Name,
    string CountryCode,
    string Timezone,
    bool IsActive,
    string? Phone,
    string? Address);

public sealed record ReplaceLocationServicesRequest(
    IReadOnlyList<Guid> ServiceIds);