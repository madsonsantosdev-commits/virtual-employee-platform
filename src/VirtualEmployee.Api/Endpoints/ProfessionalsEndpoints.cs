using Microsoft.AspNetCore.Mvc;
using VirtualEmployee.Application.Professionals;
using VirtualEmployee.Application.Professionals.CreateProfessional;
using VirtualEmployee.Application.Professionals.GetProfessionals;
using VirtualEmployee.Application.Professionals.UpdateProfessional;

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

        return endpoints;
    }
}

public sealed record CreateProfessionalRequest(
    Guid BusinessId,
    string Name);

public sealed record UpdateProfessionalRequest(
    string Name,
    bool IsActive);