using System.Collections.Concurrent;
using VirtualEmployee.Application.Payments.Gateway;

namespace VirtualEmployee.IntegrationTests.Payments.Helpers;

public sealed class RecordingPaymentGateway : IPaymentGateway
{
    private readonly ConcurrentQueue<CreateGatewayPaymentRequest> _requests = new();

    private readonly Func<
        CreateGatewayPaymentRequest,
        CancellationToken,
        Task<CreateGatewayPaymentResult>> _handler;

    public IReadOnlyList<CreateGatewayPaymentRequest> Requests =>
        _requests.ToArray();

    public RecordingPaymentGateway(
        Func<
            CreateGatewayPaymentRequest,
            CancellationToken,
            Task<CreateGatewayPaymentResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public Task<CreateGatewayPaymentResult> CreateAsync(
        CreateGatewayPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        _requests.Enqueue(request);

        return _handler(request, cancellationToken);
    }
}