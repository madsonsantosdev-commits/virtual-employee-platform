using VirtualEmployee.Application.Locations.CreateLocation;
using VirtualEmployee.Application.Locations.GetLocations;
using VirtualEmployee.Application.Locations.UpdateLocation;
using VirtualEmployee.Api.Endpoints;
using VirtualEmployee.Api.Middleware;
using VirtualEmployee.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

// Application use cases
builder.Services.AddScoped<GetLocationsHandler>();
builder.Services.AddScoped<GetLocationByIdHandler>();
builder.Services.AddScoped<CreateLocationHandler>();
builder.Services.AddScoped<UpdateLocationHandler>();

// Infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // OpenAPI document:
    // /openapi/v1.json
    app.MapOpenApi();

    // Swagger UI:
    // /swagger
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/openapi/v1.json",
            "Virtual Employee API v1");

        options.DocumentTitle = "Virtual Employee API";
    });

    // Temporary tenant resolution for local development.
    app.UseMiddleware<DevelopmentTenantMiddleware>();
}

app.UseHttpsRedirection();

app.MapLocationsEndpoints();

app.Run();

public partial class Program;