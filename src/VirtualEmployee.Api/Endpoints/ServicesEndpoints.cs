using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Services;
using VirtualEmployee.Application.Services.CreateService;
using VirtualEmployee.Application.Services.GetServices;
using VirtualEmployee.Application.Services.UpdateService;
using VirtualEmployee.Domain.Services;

namespace VirtualEmployee.Api.Endpoints;

public static class ServicesEndpoints
{
    public static IEndpointRouteBuilder MapServicesEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/services")
            .WithTags("Services");

        group.MapGet(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                [FromQuery] bool? active,
                GetServicesHandler handler,
                CancellationToken cancellationToken) =>
            {
                var services = await handler.HandleAsync(
                    active,
                    cancellationToken);

                return Results.Ok(services);
            })
            .WithName("GetServices")
            .WithSummary("Lista os serviços do tenant atual")
            .WithDescription(
                "Retorna os serviços pertencentes ao tenant atual. " +
                "O filtro active é opcional. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<IReadOnlyList<ServiceResponse>>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest);

        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetServiceByIdHandler handler,
                CancellationToken cancellationToken) =>
            {
                var service = await handler.HandleAsync(
                    id,
                    cancellationToken);

                return service is null
                    ? Results.NotFound()
                    : Results.Ok(service);
            })
            .WithName("GetServiceById")
            .WithSummary("Obtém um serviço por identificador")
            .WithDescription(
                "Retorna o serviço pertencente ao tenant atual. " +
                "Serviços inexistentes ou pertencentes a outro tenant retornam 404. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<ServiceResponse>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status404NotFound);

        group.MapPost(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                CreateServiceRequest request,
                CreateServiceHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.BusinessId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(request.Name) ||
                    request.Price < 0 ||
                    request.DurationMinutes <= 0 ||
                    !Enum.IsDefined(request.ServiceType))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid service request",
                        detail:
                            "BusinessId, name, serviceType, price and durationMinutes must be valid.");
                }

                var command = new CreateServiceCommand(
                    request.BusinessId,
                    request.Name,
                    request.ServiceType,
                    request.Price,
                    request.DurationMinutes,
                    request.ComponentServiceIds);

                var result = await handler.HandleAsync(
                    command,
                    cancellationToken);

                if (!result.IsSuccess)
                {
                    return MapOperationError(result.Error);
                }

                var service = result.Service!;

                return Results.Created(
                    $"/api/v1/services/{service.Id}",
                    service);
            })
            .WithName("CreateService")
            .WithSummary("Cria um serviço")
            .WithDescription(
                "Cria um serviço SINGLE ou COMBO para um Business pertencente ao tenant atual. " +
                "COMBO possui preço e duração próprios e somente pode conter serviços SINGLE " +
                "pertencentes ao mesmo Business. " +
                "O TenantId é obtido do contexto da requisição e não do payload. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<ServiceResponse>(
                StatusCodes.Status201Created)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status404NotFound)
            .Produces(
                StatusCodes.Status422UnprocessableEntity);

        group.MapPut(
            "/{id:guid}",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                UpdateServiceRequest request,
                UpdateServiceHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) ||
                    request.Price < 0 ||
                    request.DurationMinutes <= 0)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid service request",
                        detail:
                            "Name, price and durationMinutes must be valid.");
                }

                var command = new UpdateServiceCommand(
                    id,
                    request.Name,
                    request.Price,
                    request.DurationMinutes,
                    request.IsActive,
                    request.ComponentServiceIds);

                var result = await handler.HandleAsync(
                    command,
                    cancellationToken);

                if (!result.IsSuccess)
                {
                    return MapOperationError(result.Error);
                }

                return Results.Ok(result.Service);
            })
            .WithName("UpdateService")
            .WithSummary("Atualiza um serviço")
            .WithDescription(
                "Atualiza nome, preço, duração, estado ativo e, para COMBO, sua composição. " +
                "ServiceType não pode ser alterado após a criação. " +
                "Serviços inexistentes ou pertencentes a outro tenant retornam 404. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<ServiceResponse>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status400BadRequest)
            .Produces(
                StatusCodes.Status404NotFound)
            .Produces(
                StatusCodes.Status422UnprocessableEntity);

        return endpoints;
    }

    private static IResult MapOperationError(
        ServiceOperationError error)
    {
        return error switch
        {
            ServiceOperationError.BusinessNotFound =>
                Results.NotFound(),

            ServiceOperationError.ServiceNotFound =>
                Results.NotFound(),

            ServiceOperationError.InvalidCombo =>
                CreateUnprocessableEntity(
                    "INVALID_COMBO",
                    "Invalid combo",
                    "The service composition is invalid for its service type."),

            ServiceOperationError.ComboComponentInvalid =>
                CreateUnprocessableEntity(
                    "COMBO_COMPONENT_INVALID",
                    "Invalid combo component",
                    "Combo components must exist, be unique and belong to the same Business."),

            ServiceOperationError.ComboNestingNotAllowed =>
                CreateUnprocessableEntity(
                    "COMBO_NESTING_NOT_ALLOWED",
                    "Combo nesting is not allowed",
                    "A COMBO cannot contain another COMBO."),

            _ =>
                Results.Problem(
                    statusCode:
                        StatusCodes.Status500InternalServerError,
                    title: "Unexpected service operation error")
        };
    }

    private static IResult CreateUnprocessableEntity(
        string code,
        string title,
        string detail)
    {
        return Results.Problem(
            statusCode:
                StatusCodes.Status422UnprocessableEntity,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });
    }
}

public sealed record CreateServiceRequest(
    Guid BusinessId,
    string Name,
    ServiceType ServiceType,
    decimal Price,
    int DurationMinutes,
    IReadOnlyList<Guid>? ComponentServiceIds);

public sealed record UpdateServiceRequest(
    string Name,
    decimal Price,
    int DurationMinutes,
    bool IsActive,
    IReadOnlyList<Guid>? ComponentServiceIds);