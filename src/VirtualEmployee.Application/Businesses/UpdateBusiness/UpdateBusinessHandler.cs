namespace VirtualEmployee.Application.Businesses.UpdateBusiness;

public sealed class UpdateBusinessHandler
{
    private readonly IBusinessWriteService _businessWriteService;

    public UpdateBusinessHandler(
        IBusinessWriteService businessWriteService)
    {
        _businessWriteService = businessWriteService;
    }

    public async Task<BusinessResponse?> HandleAsync(
        UpdateBusinessCommand command,
        CancellationToken cancellationToken = default)
    {
        var business =
            await _businessWriteService.GetCurrentAsync(
                cancellationToken);

        if (business is null)
        {
            return null;
        }

        var businessTypeIsActive =
            await _businessWriteService.IsBusinessTypeActiveAsync(
                command.BusinessTypeId,
                cancellationToken);

        if (!businessTypeIsActive)
        {
            return null;
        }

        business.Update(
            command.BusinessTypeId,
            command.Name,
            command.SlotIntervalMinutes,
            command.IsActive,
            DateTimeOffset.UtcNow);

        await _businessWriteService.SaveChangesAsync(
            cancellationToken);

        return new BusinessResponse(
            business.Id,
            business.BusinessTypeId,
            business.Name,
            business.SlotIntervalMinutes,
            business.IsActive);
    }
}