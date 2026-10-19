using VirtualEmployee.Application.Appointments;
using VirtualEmployee.Application.Appointments.GetAppointments;
using VirtualEmployee.Domain.Scheduling;
using Xunit;

namespace VirtualEmployee.Application.Tests.Appointments;

public sealed class GetAppointmentsHandlerTests
{
    #region Facts

    [Fact]
    public async Task HandleAsync_WithDefaults_ShouldReturnReadServiceResponse()
    {
        var readService = new FakeReadService();
        var handler = new GetAppointmentsHandler(readService);
        var query = new GetAppointmentsQuery();

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);
        Assert.Same(readService.Response, result.Response);
        Assert.Equal(query, readService.RequestedQuery);
        Assert.Equal(1, readService.ReadCount);
    }

    [Fact]
    public async Task HandleAsync_WithFilters_ShouldPreserveFiltersAndNormalizePeriod()
    {
        var readService = new FakeReadService();
        var handler = new GetAppointmentsHandler(readService);

        var from = new DateTimeOffset(
            2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(-3));

        var query = new GetAppointmentsQuery(
            LocationId: Guid.NewGuid(),
            ProfessionalId: Guid.NewGuid(),
            CustomerId: Guid.NewGuid(),
            Status: AppointmentStatus.Pending,
            From: from,
            To: from.AddHours(2),
            Page: 2,
            PageSize: 10);

        var result = await handler.HandleAsync(query);

        Assert.True(result.IsSuccess);

        var actual = Assert.IsType<GetAppointmentsQuery>(
            readService.RequestedQuery);

        Assert.Equal(query.LocationId, actual.LocationId);
        Assert.Equal(query.ProfessionalId, actual.ProfessionalId);
        Assert.Equal(query.CustomerId, actual.CustomerId);
        Assert.Equal(query.Status, actual.Status);
        Assert.Equal(query.Page, actual.Page);
        Assert.Equal(query.PageSize, actual.PageSize);
        Assert.Equal(from.ToUniversalTime(), actual.From);
        Assert.Equal(from.AddHours(2).ToUniversalTime(), actual.To);
        Assert.Equal(TimeSpan.Zero, actual.From!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, actual.To!.Value.Offset);
    }

    [Fact]
    public async Task HandleAsync_ShouldForwardCancellationToken()
    {
        var readService = new FakeReadService();
        var handler = new GetAppointmentsHandler(readService);
        using var source = new CancellationTokenSource();

        await handler.HandleAsync(
            new GetAppointmentsQuery(),
            source.Token);

        Assert.Equal(source.Token, readService.RequestedToken);
    }

    #endregion

    #region Theories

    [Theory]
    [InlineData("location")]
    [InlineData("professional")]
    [InlineData("customer")]
    [InlineData("status")]
    [InlineData("from-only")]
    [InlineData("to-only")]
    [InlineData("equal-period")]
    [InlineData("reversed-period")]
    public async Task HandleAsync_WithInvalidFilters_ShouldRejectWithoutReading(
        string scenario)
    {
        var readService = new FakeReadService();
        var handler = new GetAppointmentsHandler(readService);

        var from = new DateTimeOffset(
            2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        var query = scenario switch
        {
            "location" => new GetAppointmentsQuery(
                LocationId: Guid.Empty),

            "professional" => new GetAppointmentsQuery(
                ProfessionalId: Guid.Empty),

            "customer" => new GetAppointmentsQuery(
                CustomerId: Guid.Empty),

            "status" => new GetAppointmentsQuery(
                Status: (AppointmentStatus)int.MaxValue),

            "from-only" => new GetAppointmentsQuery(
                From: from),

            "to-only" => new GetAppointmentsQuery(
                To: from),

            "equal-period" => new GetAppointmentsQuery(
                From: from,
                To: from),

            "reversed-period" => new GetAppointmentsQuery(
                From: from,
                To: from.AddMinutes(-1)),

            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        var result = await handler.HandleAsync(query);

        Assert.False(result.IsSuccess);
        Assert.Equal(
            GetAppointmentsError.InvalidRequest,
            result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, readService.ReadCount);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task HandleAsync_WithInvalidPagination_ShouldRejectWithoutReading(
        int page,
        int pageSize)
    {
        var readService = new FakeReadService();
        var handler = new GetAppointmentsHandler(readService);

        var result = await handler.HandleAsync(
            new GetAppointmentsQuery(
                Page: page,
                PageSize: pageSize));

        Assert.Equal(
            GetAppointmentsError.InvalidRequest,
            result.Error);
        Assert.Null(result.Response);
        Assert.Equal(0, readService.ReadCount);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(2, 50)]
    [InlineData(21474837, 100)]
    public async Task HandleAsync_WithValidPagination_ShouldRead(
        int page,
        int pageSize)
    {
        var readService = new FakeReadService();
        var handler = new GetAppointmentsHandler(readService);

        var result = await handler.HandleAsync(
            new GetAppointmentsQuery(
                Page: page,
                PageSize: pageSize));

        Assert.True(result.IsSuccess);
        Assert.NotNull(readService.RequestedQuery);
        Assert.Equal(page, readService.RequestedQuery.Page);
        Assert.Equal(pageSize, readService.RequestedQuery.PageSize);
        Assert.Equal(1, readService.ReadCount);
    }

    #endregion

    #region Helpers

    private sealed class FakeReadService : IAppointmentReadService
    {
        public GetAppointmentsResponse Response { get; } =
            new(Array.Empty<AppointmentResponse>(), 1, 50, 0);

        public GetAppointmentsQuery? RequestedQuery { get; private set; }

        public CancellationToken RequestedToken { get; private set; }

        public int ReadCount { get; private set; }

        public Task<GetAppointmentsResponse> GetAllAsync(
            GetAppointmentsQuery query,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            RequestedQuery = query;
            RequestedToken = cancellationToken;

            return Task.FromResult(Response);
        }

        public Task<AppointmentResponse?> GetByIdAsync(
            Guid appointmentId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "This fake supports listing tests only.");
        }
    }

    #endregion
}