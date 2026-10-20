using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;

namespace VirtualEmployee.Api.Endpoints;

public static class AvailabilitySlotsEndpoints
{
    public static IEndpointRouteBuilder MapAvailabilitySlotsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/availability/slots")
            .WithTags("AvailabilitySlots");

        group.MapPost(
            "/search",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                SearchAvailableSlotsRequest request,
                SearchAvailableSlotsHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.LocationId is null ||
                    request.LocationId == Guid.Empty ||
                    request.Date is null ||
                    request.Date == DateOnly.MinValue ||
                    request.ProfessionalId == Guid.Empty ||
                    request.ServiceIds is null ||
                    request.ServiceIds.Count == 0 ||
                    request.ServiceIds.Any(id => id == Guid.Empty) ||
                    request.ServiceIds.Distinct().Count() !=
                        request.ServiceIds.Count)
                {
                    return InvalidRequest();
                }

                var query = new SearchAvailableSlotsQuery(
                    request.LocationId.Value,
                    request.ServiceIds,
                    request.Date.Value,
                    request.ProfessionalId);

                var result = await handler.HandleAsync(
                    query,
                    cancellationToken);

                return result.Error switch
                {
                    SearchAvailableSlotsError.None =>
                        Results.Ok(result.Response),

                    SearchAvailableSlotsError.InvalidRequest =>
                        InvalidRequest(),

                    SearchAvailableSlotsError.LocationNotFound or
                    SearchAvailableSlotsError.BusinessNotFound or
                    SearchAvailableSlotsError.ServiceNotFound =>
                        Results.NotFound(),

                    SearchAvailableSlotsError.InactiveLocation =>
                        Conflict("Location must be active."),

                    SearchAvailableSlotsError.InactiveBusiness =>
                        Conflict("Business must be active."),

                    SearchAvailableSlotsError.InvalidServices =>
                        Conflict(
                            "Services must be active, belong to the " +
                            "Location's Business and have a valid total duration."),

                    SearchAvailableSlotsError.ServicesNotOffered =>
                        Conflict(
                            "All requested Services must be offered at the Location."),

                    SearchAvailableSlotsError.InvalidTimezone =>
                        Results.Problem(
                            statusCode: StatusCodes.Status500InternalServerError,
                            title: "Invalid location timezone",
                            detail:
                                "The configured Location timezone could not be resolved."),

                    _ => Results.Problem(
                        statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .WithName("SearchAvailableSlots")
            .WithSummary("Busca horários por unidade, serviços e data")
            .WithDescription(
                "Calcula slots para profissionais ativos e elegíveis para " +
                "todos os serviços solicitados, usando a agenda semanal " +
                "e a timezone da unidade. COMBO exige habilitação explícita " +
                "e utiliza sua própria duração. Retorna instantes UTC e " +
                "exclui inícios que já passaram, ScheduleBlocks ativos " +
                "e sobreposições com Appointments que ocupam agenda. " +
                "A ocupação do profissional considera também outras " +
                "unidades do mesmo tenant. Consultar slots não reserva horário. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces<SearchAvailableSlotsResponse>(
                StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static IResult InvalidRequest()
    {
        return Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid available slots request",
            detail:
                "LocationId, date and at least one ServiceId are required. " +
                "IDs must not be empty and ServiceIds must not contain duplicates.");
    }

    private static IResult Conflict(string detail)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Available slots conflict",
            detail: detail);
    }
}

public sealed record SearchAvailableSlotsRequest(
    Guid? LocationId,
    IReadOnlyList<Guid>? ServiceIds,
    DateOnly? Date,
    Guid? ProfessionalId);