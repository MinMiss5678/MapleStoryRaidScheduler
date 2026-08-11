using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

/// <summary>
/// 玩家 profile（period-less §8：報名 UX 大改）：常設可用時段 + 角色參戰 opt-in。
/// 直接讀寫 standing + Character.IsSeekingRaid，不吃 period/報名。舊 PlayerRegister/CharacterRegister 待 Phase 4 退場。
/// </summary>
public class ProfileService : IProfileService
{
    private readonly IPlayerAvailabilityStandingRepository _standingRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly ICharacterQuery _characterQuery;

    public ProfileService(
        IPlayerAvailabilityStandingRepository standingRepository,
        ICharacterRepository characterRepository,
        ICharacterQuery characterQuery)
    {
        _standingRepository = standingRepository;
        _characterRepository = characterRepository;
        _characterQuery = characterQuery;
    }

    public async Task<ProfileDto> GetAsync(ulong discordId)
    {
        var availabilities = await _standingRepository.GetByDiscordIdAsync(discordId);
        var characters = await _characterQuery.GetByDiscordIdAsync(discordId);

        return new ProfileDto
        {
            Availabilities = availabilities.Select(a => new PlayerAvailabilityDto
            {
                Weekday = a.Weekday,
                StartTime = a.StartTime,
                EndTime = a.EndTime
            }).ToList(),
            Characters = characters.Select(c => new ProfileCharacterDto
            {
                Id = c.Id,
                Name = c.Name,
                Job = c.Job,
                AttackPower = c.AttackPower,
                IsSeekingRaid = c.IsSeekingRaid
            }).ToList()
        };
    }

    public async Task SaveAsync(ProfileSaveCommand command)
    {
        // 擁有權：opt-in 的角色必須是本人的（否則 IDOR / 亂設別人角色）。
        var ownedIds = (await _characterQuery.GetByDiscordIdAsync(command.DiscordId)).Select(c => c.Id).ToHashSet();
        var alien = command.SeekingCharacterIds.FirstOrDefault(id => !ownedIds.Contains(id));
        if (alien != null)
            throw new NotFoundException($"Character {alien} not found");

        // 時段前線驗：EndTime 00:00 視為整天；相等且非整天 → 空窗。
        foreach (var a in command.Availabilities)
        {
            if (a.EndTime != new TimeOnly(0, 0) && a.StartTime >= a.EndTime)
                throw new BusinessException("可用時段的結束時間必須晚於開始時間。");
        }

        // 常設時段 replace-all
        await _standingRepository.DeleteByDiscordIdAsync(command.DiscordId);
        foreach (var a in command.Availabilities)
        {
            await _standingRepository.CreateAsync(new PlayerAvailability
            {
                DiscordId = command.DiscordId,
                Weekday = a.Weekday,
                StartTime = a.StartTime,
                EndTime = a.EndTime
            });
        }

        // 角色 opt-in（replace：listed=true、其餘=false）
        await _characterRepository.SetSeekingRaidForDiscordAsync(command.DiscordId, command.SeekingCharacterIds);
    }
}
