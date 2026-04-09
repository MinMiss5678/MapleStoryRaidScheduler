namespace Application.Interface;

public interface ITeamSlotCharacterService
{
    Task DeleteByDiscordIdAndPeriodAsync(ulong discordId);
}