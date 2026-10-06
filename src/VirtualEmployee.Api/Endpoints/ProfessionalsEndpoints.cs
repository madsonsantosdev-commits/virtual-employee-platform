using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Application.Professionals.CreateProfessional;
using VirtualEmployee.Application.Professionals.GetProfessionals;
using VirtualEmployee.Application.Professionals.UpdateProfessional;
using VirtualEmployee.Application.Locations;
using VirtualEmployee.Application.ProfessionalLocations;
using VirtualEmployee.Application.ProfessionalLocations.GetProfessionalLocations;
using VirtualEmployee.Application.ProfessionalLocations.ReplaceProfessionalLocations;

namespace VirtualEmployee.Api.Endpoints;

public static class ProfessionalsEndpoints
{
    public static IEndpointRouteBuilder MapProfessionalsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/professionals")
            .WithTags("Professionals");

        group.MapGet(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetProfessionalsHandler handler,
                CancellationToken cancellationToken) =>
            {
                var professionals = await handler.HandleAsync(
                    cancellationToken);

                return Results.Ok(professionals);
            })
            .WithName("GetProfessionals")
            .WithSummary("Lista os profissionais do tenant atual")
            .WithDescription(
                "Retorna profissionais ativos e inativos do tenant atual. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces<IReadOnlyList<ProfessionalResponse>>(
                StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapGet(
            "/{id:guid}",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetProfessionalByIdHandler handler,
                CancellationToken cancellationToken) =>
            {
                var professional = await handler.HandleAsync(
                    id,
                    cancellationToken);

                return professional is null
                    ? Results.NotFound()
                    : Results.Ok(professional);
            })
            .WithName("GetProfessionalById")
            .WithSummary("Obtém um profissional por identificador")
            .WithDescription(
                "Profissional inexistente ou de outro tenant retorna 404. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces<ProfessionalResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost(
            "/",
            async (
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                CreateProfessionalRequest request,
                CreateProfessionalHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.BusinessId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(request.Name) ||
                    request.Name.Trim().Length > 160)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid professional request",
                        detail:
                            "BusinessId is required. Name must contain " +
                            "between 1 and 160 characters after trimming.");
                }

                var professional = await handler.HandleAsync(
                    new CreateProfessionalCommand(
                        request.BusinessId,
                        request.Name),
                    cancellationToken);

                return professional is null
                    ? Results.NotFound()
                    : Results.Created(
                        $"/api/v1/professionals/{professional.Id}",
                        professional);
            })
            .WithName("CreateProfessional")
            .WithSummary("Cria um profissional")
            .WithDescription(
                "Cria um profissional ativo para um Business do tenant atual. " +
                "Business inexistente ou de outro tenant retorna 404. " +
                "TenantId é obtido do contexto, não do payload. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces<ProfessionalResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut(
            "/{id:guid}",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                UpdateProfessionalRequest request,
                UpdateProfessionalHandler handler,
                IProfessionalWriteService writeService,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.Name) ||
                    request.Name.Trim().Length > 160)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid professional request",
                        detail:
                            "Name must contain between 1 and 160 " +
                            "characters after trimming.");
                }

                var professional = await handler.HandleAsync(
                    new UpdateProfessionalCommand(
                        id,
                        request.Name,
                        request.IsActive),
                    cancellationToken);

                if (professional is null)
                {
                    return Results.NotFound();
                }

                await writeService.SaveChangesAsync(cancellationToken);

                return Results.Ok(professional);
            })
            .WithName("UpdateProfessional")
            .WithSummary("Atualiza um profissional")
            .WithDescription(
                "Atualiza nome e status, preservando TenantId e BusinessId. " +
                "Profissional inexistente ou de outro tenant retorna 404. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces<ProfessionalResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

                group.MapGet(
            "/{id:guid}/locations",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                GetProfessionalLocationsHandler handler,
                CancellationToken cancellationToken) =>
            {
                var locations = await handler.HandleAsync(
                    id,
                    cancellationToken);

                return locations is null
                    ? Results.NotFound()
                    : Results.Ok(locations);
            })
            .WithName("GetProfessionalLocations")
            .WithSummary("Lista as unidades vinculadas ao profissional")
            .WithDescription(
                "Retorna unidades com vínculo ativo ao profissional. " +
                "IsActive na resposta indica o estado da própria unidade. " +
                "Profissional sem vínculos ativos retorna uma lista vazia. " +
                "Profissional inexistente ou de outro tenant retorna 404. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces<IReadOnlyList<LocationResponse>>(
                StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPut(
            "/{id:guid}/locations",
            async (
                Guid id,
                [FromHeader(Name = "X-Tenant-Id")] Guid tenantId,
                ReplaceProfessionalLocationsRequest request,
                ReplaceProfessionalLocationsHandler handler,
                CancellationToken cancellationToken) =>
            {
                if (request.LocationIds is null ||
                    request.LocationIds.Any(locationId =>
                        locationId == Guid.Empty))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Invalid professional locations request",
                        detail:
                            "LocationIds is required and must not contain empty GUIDs.");
                }

                var result = await handler.HandleAsync(
                    new ReplaceProfessionalLocationsCommand(
                        id,
                        request.LocationIds),
                    cancellationToken);

                return result.Error switch
                {
                    ProfessionalLocationOperationError.None =>
                        Results.Ok(),

                    ProfessionalLocationOperationError.ProfessionalNotFound =>
                        Results.NotFound(),

                    ProfessionalLocationOperationError.LocationNotFound =>
                        Results.NotFound(),

                    ProfessionalLocationOperationError.DuplicateLocation =>
                        Results.Problem(
                            statusCode: StatusCodes.Status400BadRequest,
                            title: "Invalid professional locations request",
                            detail: "LocationIds must not contain duplicates."),

                    ProfessionalLocationOperationError.LocationFromAnotherBusiness =>
                        Results.Problem(
                            statusCode: StatusCodes.Status409Conflict,
                            title: "Location does not belong to the professional business",
                            detail:
                                "All locations must belong to the same Business as the Professional."),

                    _ =>
                        Results.Problem(
                            statusCode: StatusCodes.Status500InternalServerError,
                            title: "Unexpected professional locations error")
                };
            })
            .WithName("ReplaceProfessionalLocations")
            .WithSummary("Substitui as unidades vinculadas ao profissional")
            .WithDescription(
                "Ativa os vínculos selecionados e desativa os demais, " +
                "preservando os registros existentes. " +
                "Uma lista vazia desativa todos os vínculos do profissional. " +
                "IDs duplicados ou GUIDs vazios retornam 400. " +
                "Professional ou Location inexistente ou de outro tenant retorna 404. " +
                "Location de outro Business do mesmo tenant retorna 409. " +
                "O header X-Tenant-Id é temporário para desenvolvimento.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

        return endpoints;
    }
}

public sealed record CreateProfessionalRequest(
    Guid BusinessId,
    string Name);

public sealed record UpdateProfessionalRequest(
    string Name,
    bool IsActive);

public sealed record ReplaceProfessionalLocationsRequest(
    IReadOnlyList<Guid>? LocationIds);