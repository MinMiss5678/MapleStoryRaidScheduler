using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class CharacterRepository : ICharacterRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public CharacterRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<int> CreateAsync(Character character)
    {
        var result = await _unitOfWork.Repository<CharacterDbModel>().InsertAsync(new CharacterDbModel()
        {
            DiscordId = (long)character.DiscordId,
            Id = character.Id,
            Name = character.Name,
            Job = character.Job,
            AttackPower = character.AttackPower
        });

        return result;
    }

    public async Task<int> UpdateAsync(Character character)
    {
        var sql = new UpdateBuilder<CharacterDbModel>();
        sql.Set(x => x.Name, character.Name)
            .Set(x => x.AttackPower, character.AttackPower)
            .Where(x => x.Id == character.Id)
            .Where(x => x.DiscordId == (long)character.DiscordId);

        return await _unitOfWork.ExecuteAsync(sql);
    }

    public async Task<int> DeleteAsync(ulong discordId, string id)
    {
        var sql = new DeleteBuilder<CharacterDbModel>();
        sql.Where(x=>x.Id == id)
            .Where(x => x.DiscordId == (long)discordId);

        return await _unitOfWork.ExecuteAsync(sql);
    }
}