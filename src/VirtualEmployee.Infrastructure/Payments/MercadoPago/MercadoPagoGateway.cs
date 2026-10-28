using System.Net;
using VirtualEmployee.Application.Payments.Gateway;
using VirtualEmployee.Domain.Payments;

namespace VirtualEmployee.Infrastructure.Payments.MercadoPago;

public sealed class MercadoPagoGateway : IPaymentGateway
{
    private readonly MercadoPagoAccountResolver _accountResolver;
    private readonly MercadoPagoOrdersClient _ordersClient;

    public MercadoPagoGateway(
        MercadoPagoAccountResolver accountResolver,
        MercadoPagoOrdersClient ordersClient)
    {
        ArgumentNullException.ThrowIfNull(accountResolver);
        ArgumentNullException.ThrowIfNull(ordersClient);

        _accountResolver = accountResolver;
        _ordersClient = ordersClient;
    }

    public async Task<CreateGatewayPaymentResult> CreateAsync(
        CreateGatewayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        // Nesta etapa, somente cartões foram habilitados no adaptador.
        if (request.PaymentMethod is not
            (PaymentMethod.CreditCard or PaymentMethod.DebitCard))
        {
            return CreateGatewayPaymentResult.Rejected();
        }

        // Intenções antigas não recebem um novo horário de preparação.
        if (request.PreparedAt is null)
        {
            return CreateGatewayPaymentResult.Unknown();
        }

        var accessToken = _accountResolver.ResolveAccessToken(
            request.TenantId,
            request.BusinessId);

        var requestJson =
            MercadoPagoOrderRequestFactory.CreateJson(request);

        try
        {
            using var response = await _ordersClient.CreateAsync(
                accessToken,
                request.PaymentAttemptId,
                requestJson,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Uma falha HTTP, isoladamente, não prova que nenhuma
            // order tenha sido criada. Preserva a intenção para retry.
            if (response.StatusCode != HttpStatusCode.Created)
            {
                return CreateGatewayPaymentResult.Unknown();
            }

            var responseJson = await response.Content
                .ReadAsStringAsync(cancellationToken);

            return MercadoPagoOrderResponseReader.Read(
                responseJson,
                request);
        }
        catch (HttpRequestException)
        {
            return CreateGatewayPaymentResult.Unknown();
        }
        catch (TimeoutException)
        {
            return CreateGatewayPaymentResult.Unknown();
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested)
        {
            return CreateGatewayPaymentResult.Unknown();
        }
    }
}