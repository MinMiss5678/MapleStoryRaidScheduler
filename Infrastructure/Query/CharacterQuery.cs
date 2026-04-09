using Application.Queries;
using Domain.Entities;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Query;

public class CharacterQuery : ICharacterQuery
{
    private readonly DbContext _dbContext;

    public CharacterQuery(DbContext dbContext)
    {
        _dbContext = dbContext;
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

        return await _dbContext.QueryAsync<Character>(sql);
    }
}