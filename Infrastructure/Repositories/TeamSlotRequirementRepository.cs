using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotRequirementRepository : ITeamSlotRequirementRepository
{
    private readonly DbContext _dbContext;

    public TeamSlotRequirementRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> CreateAsync(TeamSlotRequirement requirement)
    {
        var sql = new InsertBuilder<TeamSlotRequirementDbModel>();
        sql.Set(x => x.TeamSlotId, requirement.TeamSlotId)
            .Set(x => x.Count, requirement.Count)
            .Set(x => x.MinClearCount, requirement.MinClearCount)
            .Set(x => x.MinLevel, requirement.MinLevel)
            .ReturnId();
        var requirementId = await _dbContext.ExecuteScalarAsync(sql);

        foreach (var job in requirement.Jobs)
        {
            var jobSql = new InsertBuilder<TeamSlotRequirementJobDbModel>();
            jobSql.Set(x => x.RequirementId, requirementId)
                .Set(x => x.Job, job.Job)
                .Set(x => x.MinAttackPower, job.MinAttackPower);
            await _dbContext.ExecuteAsync(jobSql);
        }

        return requirementId;
    }

    public async Task<IEnumerable<TeamSlotRequirement>> GetByTeamSlotIdAsync(int teamSlotId)
    {
        var reqSql = new QueryBuilder();
        reqSql.Select<TeamSlotRequirementDbModel>(x => new { x.Id, x.TeamSlotId, x.Count, x.MinClearCount, x.MinLevel })
            .From<TeamSlotRequirementDbModel>()
            .Where<TeamSlotRequirementDbModel>(x => x.TeamSlotId == teamSlotId);
        var requirements = (await _dbContext.QueryAsync<TeamSlotRequirement>(reqSql)).ToList();

        // 每隊需求列少（~3-5），逐列撈 Jobs 的 N+1 可忽略；1b 候選查詢若需可再改 JOIN/WHERE IN。
        foreach (var req in requirements)
        {
            var jobSql = new QueryBuilder();
            jobSql.Select<TeamSlotRequirementJobDbModel>(x => new { x.Id, x.RequirementId, x.Job, x.MinAttackPower })
                .From<TeamSlotRequirementJobDbModel>()
                .Where<TeamSlotRequirementJobDbModel>(x => x.RequirementId == req.Id);
            req.Jobs = (await _dbContext.QueryAsync<TeamSlotRequirementJob>(jobSql)).ToList();
        }

        return requirements;
    }

    public async Task DeleteByTeamSlotIdAsync(int teamSlotId)
    {
        // 子表 TeamSlotRequirementJob 由 FK ON DELETE CASCADE 連帶清
        var sql = new DeleteBuilder<TeamSlotRequirementDbModel>();
        sql.Where(x => x.TeamSlotId == teamSlotId);
        await _dbContext.ExecuteAsync(sql);
    }
}
