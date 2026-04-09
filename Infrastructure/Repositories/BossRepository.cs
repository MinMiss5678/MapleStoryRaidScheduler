using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class BossRepository : IBossRepository
{
    private readonly DbContext _dbContext;

    public BossRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IEnumerable<Boss>> GetAllAsync()
    {
        return await _dbContext.Repository<BossDbModel>().GetAllAsync<Boss>(x => new
        {
            x.Id,
            x.Name,
            x.RequireMembers,
            x.RoundConsumption
        });
    }

    public async Task<Boss?> GetByIdAsync(int bossId)
    {
        var sql = new QueryBuilder()
            .Select<BossDbModel>(x => new { x.Id, x.Name, x.RequireMembers, x.RoundConsumption })
            .From<BossDbModel>()
            .Where<BossDbModel>(x => x.Id == bossId);

        return await _dbContext.QuerySingleOrDefaultAsync<Boss>(sql);
    }

    public async Task<IEnumerable<BossTemplate>> GetTemplatesByBossIdAsync(int bossId)
    {
        var templateSql = new QueryBuilder()
            .Select<BossTemplateDbModel>(x => new { x.Id, x.BossId, x.Name })
            .From<BossTemplateDbModel>()
            .Where<BossTemplateDbModel>(x => x.BossId == bossId);

        var templates = await _dbContext.QueryAsync<BossTemplateDbModel>(templateSql);
        var result = new List<BossTemplate>();

        foreach (var template in templates)
        {
            var reqSql = new QueryBuilder()
                .Select<BossTemplateRequirementDbModel>(x => new { x.Id, x.BossTemplateId, x.JobCategory, x.Count, x.Priority })
                .From<BossTemplateRequirementDbModel>()
                .Where<BossTemplateRequirementDbModel>(x => x.BossTemplateId == template.Id);

            var reqs = await _dbContext.QueryAsync<BossTemplateRequirementDbModel>(reqSql);
            result.Add(new BossTemplate
            {
                Id = template.Id,
                BossId = template.BossId,
                Name = template.Name,
                Requirements = reqs.Select(r => new BossTemplateRequirement
                {
                    Id = r.Id,
                    BossTemplateId = r.BossTemplateId,
                    JobCategory = r.JobCategory,
                    Count = r.Count,
                    Priority = r.Priority
                }).ToList()
            });
        }

        return result;
    }

    public async Task<BossTemplate?> GetTemplateByIdAsync(int templateId)
    {
        var templateSql = new QueryBuilder()
            .Select<BossTemplateDbModel>(x => new { x.Id, x.BossId, x.Name })
            .From<BossTemplateDbModel>()
            .Where<BossTemplateDbModel>(x => x.Id == templateId);

        var template = await _dbContext.QuerySingleOrDefaultAsync<BossTemplateDbModel>(templateSql);
        if (template == null) return null;

        var reqSql = new QueryBuilder()
            .Select<BossTemplateRequirementDbModel>(x => new
                { x.Id, x.BossTemplateId, x.JobCategory, x.Count, x.Priority })
            .From<BossTemplateRequirementDbModel>()
            .Where<BossTemplateRequirementDbModel>(x => x.BossTemplateId == template.Id);

        var reqs = await _dbContext.QueryAsync<BossTemplateRequirementDbModel>(reqSql);
        return new BossTemplate
        {
            Id = template.Id,
            BossId = template.BossId,
            Name = template.Name,
            Requirements = reqs.Select(r => new BossTemplateRequirement
            {
                Id = r.Id,
                BossTemplateId = r.BossTemplateId,
                JobCategory = r.JobCategory,
                Count = r.Count,
                Priority = r.Priority
            }).ToList()
        };
    }

    public async Task<int> CreateTemplateAsync(BossTemplate template)
    {
        var templateSql = new InsertBuilder<BossTemplateDbModel>()
            .Set(x => x.BossId, template.BossId)
            .Set(x => x.Name, template.Name)
            .ReturnId();

        var templateId = await _dbContext.ExecuteScalarAsync(templateSql);

        foreach (var req in template.Requirements)
        {
            var reqSql = new InsertBuilder<BossTemplateRequirementDbModel>()
                .Set(x => x.BossTemplateId, templateId)
                .Set(x => x.JobCategory, req.JobCategory)
                .Set(x => x.Count, req.Count)
                .Set(x => x.Priority, req.Priority);
            await _dbContext.ExecuteAsync(reqSql);
        }

        return templateId;
    }

    public async Task<bool> UpdateTemplateAsync(BossTemplate template)
    {
        var templateSql = new UpdateBuilder<BossTemplateDbModel>()
            .Set(x => x.Name, template.Name)
            .Where(x => x.Id == template.Id);
        await _dbContext.ExecuteAsync(templateSql);

        // 簡單起見，先刪除所有需求再重新插入
        var deleteReqSql = new DeleteBuilder<BossTemplateRequirementDbModel>()
            .Where(x => x.BossTemplateId == template.Id);
        await _dbContext.ExecuteAsync(deleteReqSql);

        foreach (var req in template.Requirements)
        {
            var reqSql = new InsertBuilder<BossTemplateRequirementDbModel>()
                .Set(x => x.BossTemplateId, template.Id)
                .Set(x => x.JobCategory, req.JobCategory)
                .Set(x => x.Count, req.Count)
                .Set(x => x.Priority, req.Priority);
            await _dbContext.ExecuteAsync(reqSql);
        }

        return true;
    }

    public async Task<bool> DeleteTemplateAsync(int templateId)
    {
        var deleteReqSql = new DeleteBuilder<BossTemplateRequirementDbModel>()
            .Where(x => x.BossTemplateId == templateId);
        await _dbContext.ExecuteAsync(deleteReqSql);

        var deleteTemplateSql = new DeleteBuilder<BossTemplateDbModel>()
            .Where(x => x.Id == templateId);
        var result = await _dbContext.ExecuteAsync(deleteTemplateSql);

        return result > 0;
    }

    public async Task<int> CreateBossAsync(Boss boss)
    {
        var sql = new InsertBuilder<BossDbModel>()
            .Set(x => x.Name, boss.Name)
            .Set(x => x.RequireMembers, boss.RequireMembers)
            .Set(x => x.RoundConsumption, boss.RoundConsumption)
            .ReturnId();
        return await _dbContext.ExecuteScalarAsync(sql);
    }

    public async Task<bool> UpdateBossAsync(Boss boss)
    {
        var sql = new UpdateBuilder<BossDbModel>()
            .Set(x => x.Name, boss.Name)
            .Set(x => x.RequireMembers, boss.RequireMembers)
            .Set(x => x.RoundConsumption, boss.RoundConsumption)
            .Where(x => x.Id == boss.Id);
        var result = await _dbContext.ExecuteAsync(sql);
        return result > 0;
    }

    public async Task<bool> DeleteBossAsync(int bossId)
    {
        var sql = new DeleteBuilder<BossDbModel>()
            .Where(x => x.Id == bossId);
        var result = await _dbContext.ExecuteAsync(sql);
        return result > 0;
    }
}