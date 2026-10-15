using VirtualEmployee.Application.Customers.CreateCustomer;
using VirtualEmployee.Application.Customers.GetCustomerById;
using VirtualEmployee.Application.Customers.GetCustomers;

namespace VirtualEmployee.Api.Endpoints;

public static class CustomersEndpoints
{
    public static IEndpointRouteBuilder MapCustomersEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/customers")
            .WithTags("Customers");

        group.MapGet("", async (
            GetCustomersHandler handler,
            CancellationToken cancellationToken) =>
        {
            var customers = await handler.HandleAsync(
                cancellationToken);

            return Results.Ok(customers);
        });

        group.MapGet("/{customerId:guid}", async (
            Guid customerId,
            GetCustomerByIdHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (customerId == Guid.Empty)
            {
                return Results.BadRequest();
            }

            var customer = await handler.HandleAsync(
                customerId,
                cancellationToken);

            return customer is null
                ? Results.NotFound()
                : Results.Ok(customer);
        });

        group.MapPost("", async (
            CreateCustomerRequest request,
            CreateCustomerHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(
                new CreateCustomerCommand(
                    request.BusinessId,
                    request.Phone ?? string.Empty,
                    request.Name,
                    request.Email),
                cancellationToken);

            if (result.IsSuccess && result.Response is { } customer)
            {
                return Results.Created(
                    $"/api/v1/customers/{customer.Id}",
                    customer);
            }

            return result.Error switch
            {
                CreateCustomerError.InvalidRequest =>
                    Results.BadRequest(),

                CreateCustomerError.BusinessNotFound =>
                    Results.NotFound(),

                CreateCustomerError.InactiveBusiness =>
                    Results.Conflict(),

                _ => Results.Problem(
                    title: "Customer creation failed",
                    statusCode: StatusCodes.Status500InternalServerError)
            };
        });

        return app;
    }

    public sealed record CreateCustomerRequest(
        Guid BusinessId,
        string? Phone,
        string? Name = null,
        string? Email = null);
}