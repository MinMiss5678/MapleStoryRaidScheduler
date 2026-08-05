using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class CharacterBossClearRepository : ICharacterBossClearRepository
{
    private readonly DbContext _dbContext;

    public CharacterBossClearRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(CharacterBossClear clear)
    {
        await _dbContext.Repository<CharacterBossClearDbModel>().InsertAsync(new CharacterBossClearDbModel
        {
            CharacterId = clear.CharacterId,
            BossId = clear.BossId,
            ClearCount = clear.ClearCount
        });
    }

    public async Task<IEnumerable<CharacterBossClear>> GetByCharacterIdAsync(string characterId)
    {
        var sql = new QueryBuilder();
        sql.Select<CharacterBossClearDbModel>(x => new { x.Id, x.CharacterId, x.BossId, x.ClearCount })
            .From<CharacterBossClearDbModel>()
            .Where<CharacterBossClearDbModel>(x => x.CharacterId == characterId);
        return await _dbContext.QueryAsync<CharacterBossClear>(sql);
    }

    public async Task<int> DeleteByCharacterIdAsync(string characterId)
    {
        var sql = new DeleteBuilder<CharacterBossClearDbModel>();
        sql.Where(x => x.CharacterId == characterId);
        return await _dbContext.ExecuteAsync(sql);
    }
}
