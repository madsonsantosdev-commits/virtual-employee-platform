using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.GetAppointmentHistory;
using Xunit;

namespace VirtualEmployee.Application.Tests.Appointments;

public sealed class GetAppointmentHistoryHandlerTests
{
    #region Facts

    [Fact]
    public async Task HandleAsync_ShouldForwardQueryAndCancellationToken()
    {
        var response = new GetAppointmentHistoryResponse(
            Array.Empty<AppointmentHistoryEntryResponse>(),
            2,
            10,
            15);

        var service = new FakeReadService { Response = response };
        var handler = new GetAppointmentHistoryHandler(service);
        var query = new GetAppointmentHistoryQuery(
            Guid.NewGuid(),
            2,
            10);

        using var cancellation = new CancellationTokenSource();

        var result = await handler.HandleAsync(
            query,
            cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(GetAppointmentHistoryError.None, result.Error);
        Assert.Same(response, result.Response);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(query, service.RequestedQuery);
        Assert.Equal(cancellation.Token, service.RequestedToken);
    }

    [Fact]
    public async Task HandleAsync_WithMissingAppointment_ShouldReturnNotFound()
    {
        var service = new FakeReadService { Response = null };
        var handler = new GetAppointmentHistoryHandler(service);

        var result = await handler.HandleAsync(
            new GetAppointmentHistoryQuery(Guid.NewGuid()));

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetAppointmentHistoryError.AppointmentNotFound,
            result.Error);
        Assert.Null(result.Response);
        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task HandleAsync_WithEmptyHistory_ShouldReturnSuccess()
    {
        var response = new GetAppointmentHistoryResponse(
            Array.Empty<AppointmentHistoryEntryResponse>(),
            1,
            50,
            0);

        var service = new FakeReadService { Response = response };
        var handler = new GetAppointmentHistoryHandler(service);

        var result = await handler.HandleAsync(
            new GetAppointmentHistoryQuery(Guid.NewGuid()));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response);
        Assert.Empty(result.Response.Items);
        Assert.Equal(0, result.Response.TotalCount);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("empty-id")]
    [InlineData("zero-page")]
    [InlineData("negative-page")]
    [InlineData("zero-size")]
    [InlineData("negative-size")]
    [InlineData("large-size")]
    [InlineData("offset-overflow")]
    public async Task HandleAsync_WithInvalidQuery_ShouldRejectBeforeReading(
        string scenario)
    {
        var service = new FakeReadService();
        var handler = new GetAppointmentHistoryHandler(service);

        var query = new GetAppointmentHistoryQuery(Guid.NewGuid());

        query = scenario switch
        {
            "empty-id" => query with { AppointmentId = Guid.Empty },
            "zero-page" => query with { Page = 0 },
            "negative-page" => query with { Page = -1 },
            "zero-size" => query with { PageSize = 0 },
            "negative-size" => query with { PageSize = -1 },
            "large-size" => query with { PageSize = 101 },
            "offset-overflow" => query with
            {
                Page = int.MaxValue,
                PageSize = 100
            },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetAppointmentHistoryError.InvalidRequest,
            result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, service.CallCount);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(2147483647, 1)]
    public async Task HandleAsync_WithValidPagination_ShouldRead(
        int page,
        int pageSize)
    {
        var service = new FakeReadService
        {
            Response = new GetAppointmentHistoryResponse(
                Array.Empty<AppointmentHistoryEntryResponse>(),
                page,
                pageSize,
                0)
        };

        var handler = new GetAppointmentHistoryHandler(service);
        var query = new GetAppointmentHistoryQuery(
            Guid.NewGuid(),
            page,
            pageSize);

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, service.CallCount);
        Assert.Equal(query, service.RequestedQuery);
    }

    #endregion

    #region Helpers

    private sealed class FakeReadService : IAppointmentHistoryReadService
    {
        public GetAppointmentHistoryResponse? Response { get; init; }

        public int CallCount { get; private set; }
        public GetAppointmentHistoryQuery? RequestedQuery { get; private set; }
        public CancellationToken RequestedToken { get; private set; }

        public Task<GetAppointmentHistoryResponse?> GetAsync(
            GetAppointmentHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            RequestedQuery = query;
            RequestedToken = cancellationToken;

            return Task.FromResult(Response);
        }
    }

    #endregion
}