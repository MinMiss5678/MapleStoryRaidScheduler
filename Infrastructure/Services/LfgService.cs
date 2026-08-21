using Application.DTOs;
using Application.Exceptions;
using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Domain.Repositories;

namespace Infrastructure.Services;

public class LfgService : ILfgService
{
    // 找隊意圖存活時長（period-less §8 Phase 3）：即時性 → 短 TTL。
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(3);

    private readonly ILfgIntentRepository _repository;
    private readonly ICharacterQuery _characterQuery;

    public LfgService(ILfgIntentRepository repository, ICharacterQuery characterQuery)
    {
        _repository = repository;
        _characterQuery = characterQuery;
    }

    public async Task PostAsync(LfgIntentCreateCommand command)
    {
        // 必須指定一隻王（無「任意王」）。BossId 存在性由 DB FK 兜。
        if (command.BossId <= 0)
            throw new BusinessException("必須指定一隻王");
        // 擁有權：找隊用的角色必須是本人的（否則 IDOR）。
        var owned = (await _characterQuery.GetByDiscordIdAsync(command.DiscordId)).Any(c => c.Id == command.CharacterId);
        if (!owned)
            throw new NotFoundException($"Character {command.CharacterId} not found");

        await _repository.CreateAsync(new LfgIntent
        {
            DiscordId = command.DiscordId,
            CharacterId = command.CharacterId,
            BossId = command.BossId,
            ExpiresAt = DateTimeOffset.UtcNow.Add(Ttl)
        });
    }

    public Task CancelAsync(ulong discordId, int id) => _repository.DeleteAsync(discordId, id);
}
