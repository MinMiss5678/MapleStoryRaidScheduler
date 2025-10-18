using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class CharacterRegisterRepository : ICharacterRegisterRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public CharacterRegisterRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CreateAsync(CharacterRegister characterRegister)
    {
        await _unitOfWork.Repository<CharacterRegisterDbModel>().InsertAsync(new CharacterRegisterDbModel()
        {
            PlayerRegisterId = characterRegister.PlayerRegisterId,
            CharacterId = characterRegister.CharacterId,
            Job = characterRegister.Job,
            BossId = characterRegister.BossId,
            Rounds = characterRegister.Rounds
        });
    }

    public async Task UpdateAsync(CharacterRegister characterRegister)
    {
        var updateSql = new UpdateBuilder<CharacterRegisterDbModel>();
        updateSql.Set(x => x.CharacterId, characterRegister.CharacterId)
            .Set(x => x.Job, characterRegister.Job)
            .Set(x => x.BossId, characterRegister.BossId)
            .Set(x => x.Rounds, characterRegister.Rounds)
            .Where(x=> x.Id == characterRegister.Id);

        await _unitOfWork.ExecuteAsync(updateSql);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        return await _unitOfWork.Repository<CharacterRegisterDbModel>().DeleteAsync(id);
    }

    public async Task<int> DeleteByPlayerRegisterIdAsync(int playerRegisterId)
    {
        var sql = new DeleteBuilder<CharacterRegisterDbModel>();
        sql.Where(x => x.PlayerRegisterId == playerRegisterId);

        return await _unitOfWork.ExecuteAsync(sql);
    }
}