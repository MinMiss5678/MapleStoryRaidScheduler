using Domain.Entities;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Entities;
using Utils.SqlBuilder;

namespace Infrastructure.Repositories;

public class TeamSlotRepository : ITeamSlotRepository
{
    private readonly DbContext _dbContext;

    public TeamSlotRepository(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> CreateAsync(TeamSlot teamSlot)
    {
        var sql = new InsertBuilder<TeamSlotDbModel>();
        sql.Set(x => x.BossId, teamSlot.BossId)
            .Set(x => x.SlotDateTime, teamSlot.SlotDateTime.ToUniversalTime())
            .Set(x => x.IsTemporary, teamSlot.IsTemporary)
            .Set(x => x.IsPublished, teamSlot.IsPublished)
            .Set(x => x.TemplateId, teamSlot.TemplateId)
            .ReturnId();

        return await _dbContext.ExecuteScalarAsync(sql);
    }

    public async Task DeleteAsync(int id)
    {
        var charSql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        charSql.Where(x => x.TeamSlotId == id);
        await _dbContext.ExecuteAsync(charSql);

        await _dbContext.Repository<TeamSlotDbModel>().DeleteAsync(id);
    }

    public async Task<TeamSlot?> GetByIdAsync(int id)
    {
        var sql = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.IsTemporary, x.IsPublished, x.TemplateId })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.Id == id);

        var dbModel = await _dbContext.QuerySingleOrDefaultAsync<TeamSlotDbModel>(sql);
        if (dbModel == null) return null;

        var characters = await GetCharactersByTeamSlotIdAsync(id);

        return new TeamSlot
        {
            Id = dbModel.Id,
            BossId = dbModel.BossId,
            SlotDateTime = dbModel.SlotDateTime,
            IsTemporary = dbModel.IsTemporary,
            IsPublished = dbModel.IsPublished,
            TemplateId = dbModel.TemplateId,
            Characters = characters.ToList()
        };
    }

    public async Task<IEnumerable<TeamSlot>> GetByPeriodIdAsync(int periodId)
    {
        var periodSql = new QueryBuilder()
            .Select<PeriodDbModel>(x => new { x.StartDate, x.EndDate })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.Id == periodId);
        var period = await _dbContext.QuerySingleOrDefaultAsync<PeriodDbModel>(periodSql);
        if (period == null) return [];

        var sql = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.IsTemporary, x.IsPublished, x.TemplateId })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= period.StartDate && x.SlotDateTime <= period.EndDate)
            .Where<TeamSlotDbModel>(x => x.IsTemporary == false);

        var slots = (await _dbContext.QueryAsync<TeamSlotDbModel>(sql)).ToList();
        if (!slots.Any()) return [];

        var teamSlotIds = slots.Select(s => s.Id).ToList();
        var allCharacters = await GetCharactersByTeamSlotIdsAsync(teamSlotIds);
        var charactersGrouped = allCharacters.GroupBy(c => c.TeamSlotId).ToDictionary(g => g.Key, g => g.ToList());

        return slots.Select(s => new TeamSlot
        {
            Id = s.Id,
            BossId = s.BossId,
            SlotDateTime = s.SlotDateTime,
            IsTemporary = s.IsTemporary,
            IsPublished = s.IsPublished,
            TemplateId = s.TemplateId,
            Characters = charactersGrouped.GetValueOrDefault(s.Id, new List<TeamSlotCharacter>())
        });
    }

    public async Task<IEnumerable<TeamSlot>> GetIncompleteTeamsAsync(int bossId, int periodId)
    {
        var periodSql = new QueryBuilder()
            .Select<PeriodDbModel>(x => new { x.StartDate, x.EndDate })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.Id == periodId);
        var period = await _dbContext.QuerySingleOrDefaultAsync<PeriodDbModel>(periodSql);
        if (period == null) return [];

        var sql = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.IsTemporary, x.IsPublished, x.TemplateId })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.BossId == bossId)
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= period.StartDate && x.SlotDateTime <= period.EndDate)
            .Where<TeamSlotDbModel>(x => x.IsTemporary == false);

        var slots = (await _dbContext.QueryAsync<TeamSlotDbModel>(sql)).ToList();
        if (!slots.Any()) return [];

        var teamSlotIds = slots.Select(s => s.Id).ToList();
        var allCharacters = await GetCharactersByTeamSlotIdsAsync(teamSlotIds);
        var charactersGrouped = allCharacters.GroupBy(c => c.TeamSlotId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<TeamSlot>();
        foreach (var s in slots)
        {
            var characters = charactersGrouped.GetValueOrDefault(s.Id, new List<TeamSlotCharacter>());
            // 檢查是否未滿員 (至少有一個空位)
            if (characters.Any(c => c.CharacterId == null))
            {
                result.Add(new TeamSlot
                {
                    Id = s.Id,
                    BossId = s.BossId,
                    SlotDateTime = s.SlotDateTime,
                    IsTemporary = s.IsTemporary,
                    IsPublished = s.IsPublished,
                    TemplateId = s.TemplateId,
                    Characters = characters
                });
            }
        }
        return result;
    }

    public async Task<IEnumerable<TeamSlot>> GetTemporaryByPeriodIdAsync(int periodId)
    {
        var periodSql = new QueryBuilder()
            .Select<PeriodDbModel>(x => new { x.StartDate, x.EndDate })
            .From<PeriodDbModel>()
            .Where<PeriodDbModel>(x => x.Id == periodId);
        var period = await _dbContext.QuerySingleOrDefaultAsync<PeriodDbModel>(periodSql);
        if (period == null) return [];

        var sql = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.IsTemporary, x.IsPublished, x.TemplateId })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= period.StartDate && x.SlotDateTime <= period.EndDate)
            .Where<TeamSlotDbModel>(x => x.IsTemporary == true);

        var slots = (await _dbContext.QueryAsync<TeamSlotDbModel>(sql)).ToList();
        if (!slots.Any()) return [];

        var teamSlotIds = slots.Select(s => s.Id).ToList();
        var allCharacters = await GetCharactersByTeamSlotIdsAsync(teamSlotIds);
        var charactersGrouped = allCharacters.GroupBy(c => c.TeamSlotId).ToDictionary(g => g.Key, g => g.ToList());

        return slots.Select(s => new TeamSlot
        {
            Id = s.Id,
            BossId = s.BossId,
            SlotDateTime = s.SlotDateTime,
            IsTemporary = s.IsTemporary,
            IsPublished = s.IsPublished,
            TemplateId = s.TemplateId,
            Characters = charactersGrouped.GetValueOrDefault(s.Id, new List<TeamSlotCharacter>())
        });
    }

    public async Task UpdateAsync(TeamSlot teamSlot)
    {
        var sql = new UpdateBuilder<TeamSlotDbModel>();
        sql.Set(x => x.BossId, teamSlot.BossId)
            .Set(x => x.SlotDateTime, teamSlot.SlotDateTime)
            .Set(x => x.IsTemporary, teamSlot.IsTemporary)
            .Set(x => x.IsPublished, teamSlot.IsPublished)
            .Set(x => x.TemplateId, teamSlot.TemplateId)
            .Where(x => x.Id == teamSlot.Id);

        await _dbContext.ExecuteAsync(sql);

        // 更新成員：先刪除再重新插入（簡單做法）
        var deleteCharSql = new DeleteBuilder<TeamSlotCharacterDbModel>();
        deleteCharSql.Where(x => x.TeamSlotId == teamSlot.Id);
        await _dbContext.ExecuteAsync(deleteCharSql);

        foreach (var character in teamSlot.Characters)
        {
            var charSql = new InsertBuilder<TeamSlotCharacterDbModel>();
            charSql.Set(x => x.TeamSlotId, teamSlot.Id)
                .Set(x => x.DiscordId, (long)character.DiscordId)
                .Set(x => x.DiscordName, character.DiscordName)
                .Set(x => x.CharacterId, character.CharacterId)
                .Set(x => x.CharacterName, character.CharacterName)
                .Set(x => x.Job, character.Job)
                .Set(x => x.AttackPower, character.AttackPower)
                .Set(x => x.Rounds, character.Rounds)
                .Set(x => x.IsManual, character.IsManual);
            await _dbContext.ExecuteScalarAsync(charSql);
        }
    }

    private async Task<IEnumerable<TeamSlotCharacter>> GetCharactersByTeamSlotIdsAsync(IEnumerable<int> teamSlotIds)
    {
        var sql = new QueryBuilder()
            .Select<TeamSlotCharacterDbModel>(x => new
            {
                x.Id,
                x.TeamSlotId,
                x.DiscordId,
                x.DiscordName,
                x.CharacterId,
                x.CharacterName,
                x.Job,
                x.AttackPower,
                x.Rounds,
                x.IsManual
            })
            .From<TeamSlotCharacterDbModel>()
            .Where<TeamSlotCharacterDbModel>(x => teamSlotIds.Contains(x.TeamSlotId));

        var dbCharacters = await _dbContext.QueryAsync<TeamSlotCharacterDbModel>(sql);
        return dbCharacters.Select(c => new TeamSlotCharacter
        {
            Id = c.Id,
            TeamSlotId = c.TeamSlotId,
            DiscordId = (ulong)c.DiscordId,
            DiscordName = c.DiscordName,
            CharacterId = c.CharacterId,
            CharacterName = c.CharacterName,
            Job = c.Job,
            AttackPower = c.AttackPower,
            Rounds = c.Rounds,
            IsManual = c.IsManual
        });
    }

    private async Task<IEnumerable<TeamSlotCharacter>> GetCharactersByTeamSlotIdAsync(int teamSlotId)
    {
        return await GetCharactersByTeamSlotIdsAsync(new[] { teamSlotId });
    }
}