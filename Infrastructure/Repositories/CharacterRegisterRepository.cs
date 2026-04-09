using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class CharacterRegisterRepository : ICharacterRegisterRepository
{
    private readonly DbContext _dbContext;

    public CharacterRegisterRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(CharacterRegister characterRegister)
    {
        await _dbContext.Repository<CharacterRegisterDbModel>().InsertAsync(new CharacterRegisterDbModel()
        {
            PlayerRegisterId = characterRegister.PlayerRegisterId,
            CharacterId = characterRegister.CharacterId,
            BossId = characterRegister.BossId,
            Rounds = characterRegister.Rounds
        });
    }

    public async Task UpdateAsync(CharacterRegister characterRegister)
    {
        var updateSql = new UpdateBuilder<CharacterRegisterDbModel>();
        updateSql.Set(x => x.CharacterId, characterRegister.CharacterId)
            .Set(x => x.BossId, characterRegister.BossId)
            .Set(x => x.Rounds, characterRegister.Rounds)
            .Where(x=> x.Id == characterRegister.Id);

        await _dbContext.ExecuteAsync(updateSql);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        return await _dbContext.Repository<CharacterRegisterDbModel>().DeleteAsync(id);
    }

    public async Task<int> DeleteByPlayerRegisterIdAsync(int playerRegisterId)
    {
        var sql = new DeleteBuilder<CharacterRegisterDbModel>();
        sql.Where(x => x.PlayerRegisterId == playerRegisterId);

        return await _dbContext.ExecuteAsync(sql);
    }

    public async Task<int> DeleteByCharacterIdAsync(string characterId)
    {
        var sql = new DeleteBuilder<CharacterRegisterDbModel>();
        sql.Where(x => x.CharacterId == characterId);

        return await _dbContext.ExecuteAsync(sql);
    }
}