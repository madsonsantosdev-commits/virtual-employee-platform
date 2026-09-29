using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Businesses;
using VirtualEmployee.Application.Businesses.GetBusiness;
using VirtualEmployee.Application.Businesses.UpdateBusiness;

namespace VirtualEmployee.Api.Endpoints;

public static class BusinessEndpoints
{
    public static IEndpointRouteBuilder MapBusinessEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/business")
            .WithTags("Business");

        group.MapGet(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetBusinessHandler handler,
                CancellationToken cancellationToken) =>
            {
                var business = await handler.HandleAsync(
                    cancellationToken);

                return business is null
                    ? Results.NotFound()
                    : Results.Ok(business);
            })
            .WithName("GetBusiness")
            .WithSummary("Obtém o negócio do tenant atual")
            .WithDescription(
                "Retorna o Business pertencente ao tenant atual. " +
                "Quando nenhum Business é encontrado para o tenant, retorna 404. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<BusinessResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                UpdateBusinessRequest request,
                UpdateBusinessHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.BusinessTypeId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(request.Name) ||
                    request.SlotIntervalMinutes <= 0)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid business request",
                        detail:
                            "BusinessTypeId, name and a positive slotIntervalMinutes are required.");
                }

                var command = new UpdateBusinessCommand(
                    request.BusinessTypeId,
                    request.Name,
                    request.SlotIntervalMinutes,
                    request.IsActive);

                var business = await handler.HandleAsync(
                    command,
                    cancellationToken);

                return business is null
                    ? Results.NotFound()
                    : Results.Ok(business);
            })
            .WithName("UpdateBusiness")
            .WithSummary("Atualiza o negócio do tenant atual")
            .WithDescription(
                "Atualiza os dados operacionais do Business pertencente ao tenant atual. " +
                "Retorna 404 quando o Business não existe para o tenant ou quando " +
                "o BusinessType informado não existe ou está inativo. " +
                "O header X-Tenant-Id é temporário e utilizado apenas durante o desenvolvimento.")
            .Produces<BusinessResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}

public sealed record UpdateBusinessRequest(
    Guid BusinessTypeId,
    string Name,
    int SlotIntervalMinutes,
    bool IsActive);