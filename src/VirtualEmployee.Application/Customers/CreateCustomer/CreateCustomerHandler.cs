using System.Net.Mail;
using VirtualEmployee.Application.Common.Tenancy;
using VirtualEmployee.Domain.Customers;

namespace VirtualEmployee.Application.Customers.CreateCustomer;

public sealed class CreateCustomerHandler
{
    private readonly ICustomerWriteService _writeService;
    private readonly ITenantContext _tenantContext;
    private readonly TimeProvider _timeProvider;

    public CreateCustomerHandler(
        ICustomerWriteService writeService,
        ITenantContext tenantContext,
        TimeProvider? timeProvider = null)
    {
        _writeService = writeService;
        _tenantContext = tenantContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CreateCustomerResult> HandleAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.BusinessId == Guid.Empty ||
            !CustomerPhoneNormalizer.TryNormalize(
                command.Phone,
                out var normalizedPhone))
        {
            return Failure(CreateCustomerError.InvalidRequest);
        }

        var name = NormalizeOptional(command.Name);
        var email = NormalizeOptional(command.Email);

        if (email is not null &&
            (email.Any(char.IsControl) ||
             !MailAddress.TryCreate(email, out var address) ||
             !string.Equals(
                 address.Address,
                 email,
                 StringComparison.Ordinal)))
        {
            return Failure(CreateCustomerError.InvalidRequest);
        }

        var business = await _writeService.GetBusinessByIdAsync(
            command.BusinessId,
            cancellationToken);

        if (business is null ||
            business.TenantId != _tenantContext.TenantId)
        {
            return Failure(CreateCustomerError.BusinessNotFound);
        }

        if (!business.IsActive)
        {
            return Failure(CreateCustomerError.InactiveBusiness);
        }

        var currentUtc = _timeProvider.GetUtcNow().ToUniversalTime();

        var createdAt = new DateTimeOffset(
            currentUtc.Ticks - currentUtc.Ticks % 10,
            TimeSpan.Zero);

        var customer = new Customer(
            Guid.NewGuid(),
            _tenantContext.TenantId,
            business.Id,
            normalizedPhone,
            createdAt,
            name,
            email);

        await _writeService.AddAsync(customer, cancellationToken);
        await _writeService.SaveChangesAsync(cancellationToken);

        return CreateCustomerResult.Success(
            new CustomerResponse(
                customer.Id,
                customer.BusinessId,
                customer.Name,
                customer.Phone,
                customer.Email));
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static CreateCustomerResult Failure(
        CreateCustomerError error)
        => CreateCustomerResult.Failure(error);
}