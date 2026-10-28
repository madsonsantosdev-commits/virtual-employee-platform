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
using VirtualEmployee.Application.ProfessionalLocations.GetProfessionalLocations;
using VirtualEmployee.Application.ProfessionalLocations.ReplaceProfessionalLocations;
using VirtualEmployee.Application.AvailabilityRules.GetAvailabilityRules;
using VirtualEmployee.Application.AvailabilityRules.ReplaceAvailabilityRules;
using VirtualEmployee.Api.Endpoints;
using VirtualEmployee.Api.Middleware;
using VirtualEmployee.Api.Serialization;
using VirtualEmployee.Infrastructure;
using System.Text.Json.Serialization;
using VirtualEmployee.Application.ProfessionalServices.GetProfessionalServices;
using VirtualEmployee.Application.ProfessionalServices.ReplaceProfessionalServices;
using VirtualEmployee.Application.AvailabilitySlots.SearchAvailableSlots;
using VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;
using VirtualEmployee.Application.ScheduleBlocks.GetScheduleBlocks;
using VirtualEmployee.Application.ScheduleBlocks.RemoveScheduleBlock;
using VirtualEmployee.Application.Customers.CreateCustomer;
using VirtualEmployee.Application.Customers.GetCustomerById;
using VirtualEmployee.Application.Customers.GetCustomers;
using VirtualEmployee.Application.BookingQuotes.GetBookingQuote;
using VirtualEmployee.Application.Appointments.CreateAppointment;
using VirtualEmployee.Application.Appointments.GetAppointmentById;
using VirtualEmployee.Application.Appointments.GetAppointments;
using VirtualEmployee.Application.Appointments.CancelAppointment;
using VirtualEmployee.Application.Appointments.RescheduleAppointment;
using VirtualEmployee.Application.Appointments.GetAppointmentHistory;
using VirtualEmployee.Application.Payments.CreatePaymentAttempt;
using VirtualEmployee.Infrastructure.Payments;
using VirtualEmployee.Api.Configuration;

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
builder.Services.AddScoped<GetProfessionalLocationsHandler>();
builder.Services.AddScoped<ReplaceProfessionalLocationsHandler>();
builder.Services.AddScoped<GetProfessionalServicesHandler>();
builder.Services.AddScoped<ReplaceProfessionalServicesHandler>();
builder.Services.AddScoped<GetAvailabilityRulesHandler>();
builder.Services.AddScoped<ReplaceAvailabilityRulesHandler>();
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddScoped<SearchAvailableSlotsHandler>();
builder.Services.AddScoped<CreateScheduleBlockHandler>();
builder.Services.AddScoped<GetScheduleBlocksHandler>();
builder.Services.AddScoped<RemoveScheduleBlockHandler>();
builder.Services.AddScoped<CreateCustomerHandler>();
builder.Services.AddScoped<GetCustomersHandler>();
builder.Services.AddScoped<GetCustomerByIdHandler>();
builder.Services.AddScoped<GetBookingQuoteHandler>();
builder.Services.AddScoped<CreateAppointmentHandler>();
builder.Services.AddScoped<GetAppointmentByIdHandler>();
builder.Services.AddScoped<GetAppointmentsHandler>();
builder.Services.AddScoped<CancelAppointmentHandler>();
builder.Services.AddScoped<RescheduleAppointmentHandler>();
builder.Services.AddScoped<GetAppointmentHistoryHandler>();

// Infrastructure
builder.Services.AddInfrastructure(builder.Configuration);

var paymentsEnabled = builder.Configuration.GetValue<bool>(
    "Payments:Enabled",
    false);

if (paymentsEnabled)
{
    builder.Services.AddScoped<CreatePaymentAttemptHandler>();

    builder.Services.AddScoped<
        ICreatePaymentAttemptService,
        CreatePaymentAttemptService>();

    var mercadoPagoEnabled = builder.Configuration.GetValue<bool>(
        "Payments:MercadoPago:Enabled",
        false);

    if (mercadoPagoEnabled)
    {
        builder.Services.AddMercadoPagoGateway(
            builder.Configuration);
    }
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
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
app.MapAvailabilityRulesEndpoints();
app.MapAvailabilitySlotsEndpoints();
app.MapScheduleBlocksEndpoints();
app.MapCustomersEndpoints();
app.MapBookingQuotesEndpoints();
app.MapAppointmentsEndpoints();

if (paymentsEnabled)
{
    app.MapPaymentAttemptsEndpoints();
}
app.Run();

public partial class Program;