using Application.DTOs;
using Domain.Entities;

namespace Application.Interface;

public interface ITeamSlotService
{
    Task<IEnumerable<TeamSlot>> GetByBossIdAsync(int bossId);
    Task<IEnumerable<TeamSlot>> GetByDiscordIdAsync(ulong discord);
    Task UpdateAsync(TeamSlotUpdateRequest teamSlotUpdateRequest, bool isAdmin, ulong currentDiscordId);
}