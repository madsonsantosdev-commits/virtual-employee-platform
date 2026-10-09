using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.AvailabilityRules;
using VirtualEmployee.Application.AvailabilityRules.GetAvailabilityRules;
using VirtualEmployee.Application.AvailabilityRules.ReplaceAvailabilityRules;

namespace VirtualEmployee.Api.Endpoints;

public static class AvailabilityRulesEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilityRulesEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(
                "/api/v1/locations/{locationId:guid}" +
                "/professionals/{professionalId:guid}/availability-rules")
            .WithTags("Availability Rules");

        group.MapGet(
            "",
            async (
                Guid locationId,
                Guid professionalId,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetAvailabilityRulesHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (locationId == Guid.Empty ||
                    professionalId == Guid.Empty)
                {
                    return InvalidRequest(
                        "LocationId and ProfessionalId must not be empty.");
                }

                var rules = await handler.HandleAsync(
                    locationId,
                    professionalId,
                    cancellationToken);

                return rules is null
                    ? Results.NotFound()
                    : Results.Ok(rules);
            })
            .WithName("GetAvailabilityRules")
            .WithSummary("Consulta a agenda-base do profissional na unidade")
            .WithDescription(
                "Retorna janelas ativas ordenadas por dia e horário. " +
                "Os horários são locais ao timezone da Location. " +
                "A consulta administrativa permite recursos inativos. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces<IReadOnlyList<AvailabilityRuleResponse>>(
                StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut(
            "",
            async (
                Guid locationId,
                Guid professionalId,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                ReplaceAvailabilityRulesRequest request,
                ReplaceAvailabilityRulesHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (locationId == Guid.Empty ||
                    professionalId == Guid.Empty)
                {
                    return InvalidRequest(
                        "LocationId and ProfessionalId must not be empty.");
                }

                if (request.Rules is null ||
                    request.Rules.Any(rule =>
                        rule is null ||
                        rule.DayOfWeek is null ||
                        rule.StartTime is null ||
                        rule.EndTime is null))
                {
                    return InvalidRequest(
                        "Rules is required. Each rule must contain " +
                        "DayOfWeek, StartTime and EndTime.");
                }

                var rules = request.Rules
                    .Select(rule => new AvailabilityRuleInput(
                        (DayOfWeek)rule!.DayOfWeek!.Value,
                        rule.StartTime!.Value,
                        rule.EndTime!.Value))
                    .ToArray();

                var result = await handler.HandleAsync(
                    new ReplaceAvailabilityRulesCommand(
                        locationId,
                        professionalId,
                        rules),
                    cancellationToken);

                return result.Error switch
                {
                    AvailabilityRuleOperationError.None =>
                        Results.Ok(),

                    AvailabilityRuleOperationError.LocationNotFound or
                    AvailabilityRuleOperationError.ProfessionalNotFound or
                    AvailabilityRuleOperationError.ProfessionalLocationNotFound =>
                        Results.NotFound(),

                    AvailabilityRuleOperationError.InvalidDayOfWeek =>
                        InvalidRequest(
                            "DayOfWeek must be between 0 and 6."),

                    AvailabilityRuleOperationError.InvalidTimeWindow =>
                        InvalidRequest(
                            "StartTime must be earlier than EndTime."),

                    AvailabilityRuleOperationError.OverlappingRules =>
                        InvalidRequest(
                            "Rules must not overlap on the same day."),

                    AvailabilityRuleOperationError.ProfessionalFromAnotherBusiness =>
                        Conflict(
                            "Professional and Location must belong " +
                            "to the same Business."),

                    AvailabilityRuleOperationError.InactiveLocation =>
                        Conflict("Location must be active."),

                    AvailabilityRuleOperationError.InactiveProfessional =>
                        Conflict("Professional must be active."),

                    AvailabilityRuleOperationError.InactiveProfessionalLocation =>
                        Conflict("ProfessionalLocation must be active."),

                    _ => Results.Problem(
                        statusCode:
                            StatusCodes.Status500InternalServerError)
                };
            })
            .WithName("ReplaceAvailabilityRules")
            .WithSummary("Substitui a agenda-base do profissional na unidade")
            .WithDescription(
                "Recebe rules com dayOfWeek de 0 (domingo) a 6 (sábado) " +
                "e horários locais startTime/endTime. " +
                "Exige recursos e vínculo ativos, do mesmo Business. " +
                "Rejeita janelas sobrepostas e permite janelas consecutivas. " +
                "Uma lista vazia desativa todas as regras. " +
                "Preserva registros e datas das janelas sem alteração. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static IResult InvalidRequest(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid availability rules request",
            detail: detail);
    }

    private static IResult Conflict(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Availability rules conflict",
            detail: detail);
    }
}

public sealed record ReplaceAvailabilityRulesRequest(
    IReadOnlyList<AvailabilityRuleRequest?>? Rules);

public sealed record AvailabilityRuleRequest(
    int? DayOfWeek,
    TimeOnly? StartTime,
    TimeOnly? EndTime);