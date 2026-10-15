using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.ScheduleBlocks;
using VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;
using VirtualEmployee.Application.ScheduleBlocks.GetScheduleBlocks;
using VirtualEmployee.Application.ScheduleBlocks.RemoveScheduleBlock;

namespace VirtualEmployee.Api.Endpoints;

public static class ScheduleBlocksEndpoints
{
    public static IEndpointRouteBuilder MapScheduleBlocksEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/schedule-blocks")
            .WithTags("ScheduleBlocks");

        group.MapGet(
            "",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                [FromQuery] Guid? locationId,
                [FromQuery] DateTimeOffset? from,
                [FromQuery] DateTimeOffset? to,
                [FromQuery] Guid? professionalId,
                GetScheduleBlocksHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (locationId is null ||
                    locationId == Guid.Empty ||
                    professionalId == Guid.Empty ||
                    from is null ||
                    to is null ||
                    from.Value >= to.Value)
                {
                    return InvalidRequest(
                        "LocationId and a valid period are required. " +
                        "IDs must not be empty and from must precede to.");
                }

                var blocks = await handler.HandleAsync(
                    new GetScheduleBlocksQuery(
                        locationId.Value,
                        from.Value,
                        to.Value,
                        professionalId),
                    cancellationToken);

                return blocks is null
                    ? Results.NotFound()
                    : Results.Ok(blocks);
            })
            .WithName("GetScheduleBlocks")
            .WithSummary("Consulta bloqueios ativos por unidade e período")
            .Produces<IReadOnlyList<ScheduleBlockResponse>>(
                StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(
            "",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
                CreateScheduleBlockRequest request,
                CreateScheduleBlockHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.LocationId is null ||
                    request.StartsAt is null ||
                    request.EndsAt is null)
                {
                    return InvalidRequest(
                        "LocationId, StartsAt and EndsAt are required.");
                }

                var result = await handler.HandleAsync(
                    new CreateScheduleBlockCommand(
                        request.LocationId.Value,
                        request.ProfessionalId,
                        request.StartsAt.Value,
                        request.EndsAt.Value,
                        idempotencyKey ?? string.Empty),
                    cancellationToken);

                return result.Error switch
                {
                    CreateScheduleBlockError.None =>
                        Results.Json(
                            result.Response,
                            statusCode: StatusCodes.Status201Created),

                    CreateScheduleBlockError.InvalidRequest =>
                        InvalidRequest(
                            "IDs and interval must be valid. " +
                            "Idempotency-Key is required, must have at most " +
                            "200 characters and contain no whitespace or control characters."),

                    CreateScheduleBlockError.LocationNotFound or
                    CreateScheduleBlockError.BusinessNotFound or
                    CreateScheduleBlockError.ProfessionalNotFound or
                    CreateScheduleBlockError.ProfessionalLocationNotFound =>
                        Results.NotFound(),

                    CreateScheduleBlockError.IdempotencyConflict =>
                        Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Idempotency conflict",
                            detail:
                                "The key was already used with different request content.",
                            extensions: new Dictionary<string, object?>
                            {
                                ["code"] = "IDEMPOTENCY_CONFLICT"
                            }),

                    CreateScheduleBlockError.ProfessionalFromAnotherBusiness =>
                        Conflict(
                            "Professional and Location must belong to the same Business."),

                    CreateScheduleBlockError.InactiveLocation =>
                        Conflict("Location must be active."),

                    CreateScheduleBlockError.InactiveBusiness =>
                        Conflict("Business must be active."),

                    CreateScheduleBlockError.InactiveProfessional =>
                        Conflict("Professional must be active."),

                    CreateScheduleBlockError.InactiveProfessionalLocation =>
                        Conflict("ProfessionalLocation must be active."),

                    _ => Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .WithName("CreateScheduleBlock")
            .WithSummary("Cria um bloqueio com idempotência")
            .WithDescription(
                "ProfessionalId ausente ou null bloqueia toda a unidade. " +
                "A mesma chave e conteúdo retornam a resposta original. " +
                "Não move agendamentos automaticamente. " +
                "A avaliação de impacto sobre Appointments ainda está pendente.")
            .Produces<ScheduleBlockResponse>(
                StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete(
            "/{blockId:guid}",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                Guid blockId,
                RemoveScheduleBlockHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (blockId == Guid.Empty)
                {
                    return InvalidRequest(
                        "Schedule block id must not be empty.");
                }

                var removed = await handler.HandleAsync(
                    blockId,
                    cancellationToken);

                return removed
                    ? Results.NoContent()
                    : Results.NotFound();
            })
            .WithName("RemoveScheduleBlock")
            .WithSummary("Desativa um bloqueio preservando o registro")
            .WithDescription(
                "Retorna 204 para bloqueios existentes, inclusive já inativos. " +
                "Retorna 404 para bloqueio inexistente ou de outro tenant. " +
                "A repetição não altera novamente UpdatedAt.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static IResult InvalidRequest(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid schedule block request",
            detail: detail);
    }

    private static IResult Conflict(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Schedule block conflict",
            detail: detail);
    }
}

public sealed record CreateScheduleBlockRequest(
    Guid? LocationId,
    Guid? ProfessionalId,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt);