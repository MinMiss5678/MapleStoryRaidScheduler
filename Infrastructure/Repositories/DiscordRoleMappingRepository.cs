using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class DiscordRoleMappingRepository : IDiscordRoleMappingRepository
{
    private readonly DbContext _dbContext;

    public DiscordRoleMappingRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string?> ResolveRoleAsync(IEnumerable<ulong> discordRoleIds)
    {
        var roleIds = discordRoleIds?.Select(id => (long)id).ToArray() ?? Array.Empty<long>();
        if (roleIds.Length == 0) return null;

        var sql = new QueryBuilder();
        sql.Select<DiscordRoleMappingDbModel>(x => new { x.Role })
            .From<DiscordRoleMappingDbModel>()
            .Where<DiscordRoleMappingDbModel>(x=> roleIds.Contains(x.DiscordRoleId))
            .OrderByDescending<DiscordRoleMappingDbModel>(x=> x.Priority)
            .Limit(1);

        var result = await _dbContext.QuerySingleOrDefaultAsync<DiscordRoleMappingDbModel>(sql);
        return result?.Role;
    }
}
