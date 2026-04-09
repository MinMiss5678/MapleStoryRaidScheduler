using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotCharacterRepository : ITeamSlotCharacterRepository
{
    private readonly DbContext _dbContext;

    public TeamSlotCharacterRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CreateAsync(TeamSlotCharacter teamSlot)
    {
        await _dbContext.Repository<TeamSlotCharacterDbModel>().InsertAsync(new TeamSlotCharacterDbModel
        {
            TeamSlotId = teamSlot.TeamSlotId,
            CharacterId = teamSlot.CharacterId
        });
    }

    public async Task DeleteByTeamSlotIdAsync(int teamSlotId)
    {
        var sql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        sql.Where(x => x.TeamSlotId == teamSlotId);

        await _dbContext.ExecuteAsync(sql);
    }

    public async Task DeleteCharacterAsync(TeamSlotCharacter teamSlotCharacter)
    {
        var sql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        sql.Where(x => x.TeamSlotId == teamSlotCharacter.TeamSlotId)
            .Where(x => x.CharacterId == teamSlotCharacter.CharacterId);

        await _dbContext.ExecuteAsync(sql);
    }
}