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
            .Set(x => x.Source, teamSlot.Source)
            .Set(x => x.TemplateId, teamSlot.TemplateId)
            // leader-led 新欄：舊 auto-assign 路徑不帶（PeriodId=0/其餘 null）→ 寫 NULL、不變行為；
            // PeriodId 是 Period FK，0 會 FK 違反，故 0 一律轉 NULL（只有 leader 開隊帶真值）。
            .Set(x => x.PeriodId, teamSlot.PeriodId > 0 ? (int?)teamSlot.PeriodId : null)
            .Set(x => x.LeaderDiscordId, teamSlot.LeaderDiscordId.HasValue ? (long?)teamSlot.LeaderDiscordId.Value : null)
            .Set(x => x.Description, teamSlot.Description)
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
        // 原本是「查 TeamSlot」+「查 Characters」兩次往返，合併成一次 LEFT JOIN：
        // 併發控制迴圈裡每個既有隊伍都會呼叫一次，逐隊各打兩次是可避免的 N+1（見 architecture.md）。
        var sql = new QueryBuilder()
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.Source, x.TemplateId, x.LeaderDiscordId })
            .Select<TeamSlotCharacterDbModel>(x => new
            {
                CharacterRowId = x.Id,
                x.DiscordId,
                x.DiscordName,
                x.CharacterId,
                x.CharacterName,
                x.Job,
                x.AttackPower,
                x.Rounds,
                x.IsManual
            }, "b")
            .From<TeamSlotDbModel>()
            .LeftJoin<TeamSlotCharacterDbModel>("""a."Id" = b."TeamSlotId" """)
            .Where<TeamSlotDbModel>(x => x.Id == id);

        var rows = (await _dbContext.QueryAsync<TeamSlotWithCharacterRow>(sql)).ToList();
        if (rows.Count == 0) return null;

        var first = rows[0];
        // 空隊的 LEFT JOIN 會產生 CharacterRowId=0 的 ghost row（Dapper 對 int? NULL 映射成 int 的慣例），需過濾掉
        var characters = rows
            .Where(r => r.CharacterRowId != 0)
            .Select(r => new TeamSlotCharacter
            {
                Id = r.CharacterRowId,
                TeamSlotId = id,
                DiscordId = (ulong)r.DiscordId,
                DiscordName = r.DiscordName ?? "",
                CharacterId = r.CharacterId,
                CharacterName = r.CharacterName,
                Job = r.Job ?? "",
                AttackPower = r.AttackPower,
                Rounds = r.Rounds,
                IsManual = r.IsManual
            })
            .ToList();

        return new TeamSlot
        {
            Id = first.Id,
            BossId = first.BossId,
            SlotDateTime = first.SlotDateTime,
            Source = first.Source,
            TemplateId = first.TemplateId,
            LeaderDiscordId = first.LeaderDiscordId.HasValue ? (ulong?)first.LeaderDiscordId.Value : null,
            Characters = characters
        };
    }

    private class TeamSlotWithCharacterRow
    {
        public int Id { get; set; }
        public int BossId { get; set; }
        public DateTimeOffset SlotDateTime { get; set; }
        public string Source { get; set; } = "";
        public int? TemplateId { get; set; }
        public long? LeaderDiscordId { get; set; }
        public int CharacterRowId { get; set; }
        public long DiscordId { get; set; }
        public string? DiscordName { get; set; }
        public string? CharacterId { get; set; }
        public string? CharacterName { get; set; }
        public string? Job { get; set; }
        public int AttackPower { get; set; }
        public int Rounds { get; set; }
        public bool IsManual { get; set; }
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
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.Source, x.TemplateId })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= period.StartDate && x.SlotDateTime <= period.EndDate)
            .Where<TeamSlotDbModel>(x => x.Source == TeamSlotSource.Auto);

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
            Source = s.Source,
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
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.Source, x.TemplateId })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.BossId == bossId)
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= period.StartDate && x.SlotDateTime <= period.EndDate)
            .Where<TeamSlotDbModel>(x => x.Source == TeamSlotSource.Auto);

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
                    Source = s.Source,
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
            .Select<TeamSlotDbModel>(x => new { x.Id, x.BossId, x.SlotDateTime, x.Source, x.TemplateId })
            .From<TeamSlotDbModel>()
            .Where<TeamSlotDbModel>(x => x.SlotDateTime >= period.StartDate && x.SlotDateTime <= period.EndDate)
            .Where<TeamSlotDbModel>(x => x.Source == TeamSlotSource.Admin);

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
            Source = s.Source,
            TemplateId = s.TemplateId,
            Characters = charactersGrouped.GetValueOrDefault(s.Id, new List<TeamSlotCharacter>())
        });
    }

    public async Task UpdateAsync(TeamSlot teamSlot)
    {
        var sql = new UpdateBuilder<TeamSlotDbModel>();
        sql.Set(x => x.BossId, teamSlot.BossId)
            .Set(x => x.SlotDateTime, teamSlot.SlotDateTime)
            .Set(x => x.Source, teamSlot.Source)
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
}
