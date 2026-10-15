using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using VirtualEmployee.Application.ScheduleBlocks;
using VirtualEmployee.Application.ScheduleBlocks.CreateScheduleBlock;
using VirtualEmployee.Domain.Scheduling;
using VirtualEmployee.Infrastructure.Persistence;
using VirtualEmployee.Infrastructure.Persistence.Idempotency;

namespace VirtualEmployee.Infrastructure.ScheduleBlocks;

public sealed class ScheduleBlockCreationService
    : IScheduleBlockCreationService
{
    private const string Operation = "CreateScheduleBlock";

    private readonly AppDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public ScheduleBlockCreationService(
        AppDbContext dbContext,
        TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<CreateScheduleBlockResult> CreateAsync(
        CreateScheduleBlockCommand command,
        CancellationToken cancellationToken = default)
    {
        var requestHash = ScheduleBlockRequestHasher.Compute(command);

        var existingRecord = await FindRecordAsync(
            command.IdempotencyKey,
            cancellationToken);

        if (existingRecord is not null)
        {
            return Replay(existingRecord, requestHash);
        }

        var validationError = await ValidateResourcesAsync(
            command,
            cancellationToken);

        if (validationError != CreateScheduleBlockError.None)
        {
            return CreateScheduleBlockResult.Failure(validationError);
        }

        var currentUtc = _timeProvider.GetUtcNow().ToUniversalTime();

        // Um microssegundo corresponde a 10 ticks do .NET.
        var now = new DateTimeOffset(
            currentUtc.Ticks - currentUtc.Ticks % 10,
            TimeSpan.Zero);

        var block = new ScheduleBlock(
            Guid.NewGuid(),
            _dbContext.CurrentTenantId,
            command.LocationId,
            command.ProfessionalId,
            command.StartsAt,
            command.EndsAt,
            now);

        var response = new ScheduleBlockResponse(
            block.Id,
            block.LocationId,
            block.ProfessionalId,
            block.StartsAt,
            block.EndsAt,
            block.IsActive,
            block.CreatedAt,
            block.UpdatedAt);

        var record = new IdempotencyRecord(
            _dbContext.CurrentTenantId,
            Operation,
            command.IdempotencyKey,
            requestHash,
            JsonSerializer.Serialize(response),
            now);

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    cancellationToken);

            _dbContext.ScheduleBlocks.Add(block);
            _dbContext.IdempotencyRecords.Add(record);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreateScheduleBlockResult.Success(response);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "PK_idempotency_records"
            })
        {
            // A transação perdedora foi descartada antes de entrar no catch.
            // Retira somente as entidades desta tentativa do tracking.
            _dbContext.Entry(block).State = EntityState.Detached;
            _dbContext.Entry(record).State = EntityState.Detached;

            var winnerRecord = await FindRecordAsync(
                command.IdempotencyKey,
                cancellationToken);

            if (winnerRecord is null)
            {
                throw;
            }

            return Replay(winnerRecord, requestHash);
        }
    }

    private Task<IdempotencyRecord?> FindRecordAsync(
        string key,
        CancellationToken cancellationToken)
    {
        return _dbContext.IdempotencyRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record =>
                    record.TenantId == _dbContext.CurrentTenantId &&
                    record.Operation == Operation &&
                    record.Key == key,
                cancellationToken);
    }

    private static CreateScheduleBlockResult Replay(
        IdempotencyRecord record,
        string requestHash)
    {
        if (!string.Equals(
                record.RequestHash,
                requestHash,
                StringComparison.Ordinal))
        {
            return CreateScheduleBlockResult.Failure(
                CreateScheduleBlockError.IdempotencyConflict);
        }

        var response =
            JsonSerializer.Deserialize<ScheduleBlockResponse>(
                record.ResponseJson)
            ?? throw new InvalidOperationException(
                "Stored idempotency response could not be read.");

        return CreateScheduleBlockResult.Success(
            response,
            isReplay: true);
    }

    private async Task<CreateScheduleBlockError> ValidateResourcesAsync(
        CreateScheduleBlockCommand command,
        CancellationToken cancellationToken)
    {
        var location = await _dbContext.Locations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == command.LocationId,
                cancellationToken);

        if (location is null)
        {
            return CreateScheduleBlockError.LocationNotFound;
        }

        if (!location.IsActive)
        {
            return CreateScheduleBlockError.InactiveLocation;
        }

        var business = await _dbContext.Businesses
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == location.BusinessId,
                cancellationToken);

        if (business is null)
        {
            return CreateScheduleBlockError.BusinessNotFound;
        }

        if (!business.IsActive)
        {
            return CreateScheduleBlockError.InactiveBusiness;
        }

        if (command.ProfessionalId is not Guid professionalId)
        {
            return CreateScheduleBlockError.None;
        }

        var professional = await _dbContext.Professionals
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == professionalId,
                cancellationToken);

        if (professional is null)
        {
            return CreateScheduleBlockError.ProfessionalNotFound;
        }

        if (professional.BusinessId != location.BusinessId)
        {
            return CreateScheduleBlockError.ProfessionalFromAnotherBusiness;
        }

        if (!professional.IsActive)
        {
            return CreateScheduleBlockError.InactiveProfessional;
        }

        var link = await _dbContext.ProfessionalLocations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x =>
                    x.ProfessionalId == professionalId &&
                    x.LocationId == location.Id,
                cancellationToken);

        if (link is null)
        {
            return CreateScheduleBlockError.ProfessionalLocationNotFound;
        }

        return link.IsActive
            ? CreateScheduleBlockError.None
            : CreateScheduleBlockError.InactiveProfessionalLocation;
    }
}