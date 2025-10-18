using Application.Interface;
using Application.Queries;
using Domain.Entities;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class CharacterQuery : ICharacterQuery
{
    private readonly IUnitOfWork _unitOfWork;

    public CharacterQuery(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<Character>> GetByDiscordIdAsync(ulong discordId)
    {
        var sql = new QueryBuilder();
        sql.Select<CharacterDbModel>(x => new
            {
                x.Id,
                x.DiscordId,
                x.Name,
                x.Job,
                x.AttackPower
            })
            .From<CharacterDbModel>()
            .Where<CharacterDbModel>(x => x.DiscordId == (long)discordId);

        return await _unitOfWork.QueryAsync<Character>(sql);
    }
}