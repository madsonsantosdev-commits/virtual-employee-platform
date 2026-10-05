using VirtualEmployee.Application.Locations.CreateLocation;
using VirtualEmployee.Application.Locations.GetLocations;
using VirtualEmployee.Application.Locations.UpdateLocation;
using VirtualEmployee.Application.Businesses.GetBusiness;
using VirtualEmployee.Application.Businesses.UpdateBusiness;
using VirtualEmployee.Application.Services.CreateService;
using VirtualEmployee.Application.Services.GetServices;
using VirtualEmployee.Application.Services.UpdateService;
using VirtualEmployee.Application.LocationServices.GetLocationServices;
using VirtualEmployee.Application.LocationServices.ReplaceLocationServices;
using VirtualEmployee.Application.Professionals.CreateProfessional;
using VirtualEmployee.Application.Professionals.GetProfessionals;
using VirtualEmployee.Application.Professionals.UpdateProfessional;
using VirtualEmployee.Api.Endpoints;
using VirtualEmployee.Api.Middleware;
using VirtualEmployee.Api.Serialization;
using VirtualEmployee.Infrastructure;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI
builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(
        new JsonStringEnumConverter(new UpperCaseJsonNamingPolicy()));
});

// Application use cases
builder.Services.AddScoped<GetLocationsHandler>();
builder.Services.AddScoped<GetLocationByIdHandler>();
builder.Services.AddScoped<CreateLocationHandler>();
builder.Services.AddScoped<UpdateLocationHandler>();
builder.Services.AddScoped<GetBusinessHandler>();
builder.Services.AddScoped<UpdateBusinessHandler>();
builder.Services.AddScoped<GetServicesHandler>();
builder.Services.AddScoped<GetServiceByIdHandler>();
builder.Services.AddScoped<CreateServiceHandler>();
builder.Services.AddScoped<UpdateServiceHandler>();
builder.Services.AddScoped<GetLocationServicesHandler>();
builder.Services.AddScoped<ReplaceLocationServicesHandler>();
builder.Services.AddScoped<GetProfessionalsHandler>();
builder.Services.AddScoped<GetProfessionalByIdHandler>();
builder.Services.AddScoped<CreateProfessionalHandler>();
builder.Services.AddScoped<UpdateProfessionalHandler>();

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
app.MapBusinessEndpoints();
app.MapServicesEndpoints();
app.MapProfessionalsEndpoints();

app.Run();

public partial class Program;