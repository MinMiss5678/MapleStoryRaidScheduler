using Application.DTOs;

namespace Application.Interface;

public interface ITeamSlotService
{
    Task<IEnumerable<TeamSlotDto>> GetByBossIdAsync(int bossId);
    Task<IEnumerable<TeamSlotDto>> GetByDiscordIdAsync(ulong discordId);
    Task UpdateAsync(TeamSlotUpdateRequest teamSlotUpdateRequest, bool isAdmin, ulong currentDiscordId);
}
