using Application.Interface;
using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotCharacterRepository : ITeamSlotCharacterRepository
{
    private readonly IUnitOfWork _unitOfWork;

    public TeamSlotCharacterRepository(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task CreateAsync(TeamSlotCharacter teamSlot)
    {
        await _unitOfWork.Repository<TeamSlotCharacterDbModel>().InsertAsync(new TeamSlotCharacterDbModel
        {
            TeamSlotId = teamSlot.TeamSlotId,
            CharacterId = teamSlot.CharacterId
        });
    }

    public async Task DeleteByTeamSlotIdAsync(int teamSlotId)
    {
        var sql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        sql.Where(x => x.TeamSlotId == teamSlotId);

        await _unitOfWork.ExecuteAsync(sql);
    }

    public async Task DeleteCharacterAsync(TeamSlotCharacter teamSlotCharacter)
    {
        var sql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        sql.Where(x => x.TeamSlotId == teamSlotCharacter.TeamSlotId)
            .Where(x => x.CharacterId == teamSlotCharacter.CharacterId);

        await _unitOfWork.ExecuteAsync(sql);
    }
}