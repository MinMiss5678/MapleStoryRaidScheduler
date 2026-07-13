using Domain.Entities;
using Infrastructure.Repositories;
using Xunit;

namespace Test.Integration;

[Collection("pg")]
[Trait("Category", "Integration")]
public class TeamSlotCharacterRepositoryIntegrationTests
{
    private readonly PostgresFixture _fx;
    public TeamSlotCharacterRepositoryIntegrationTests(PostgresFixture fx) => _fx = fx;

    [Fact]
    public async Task DeleteCharacter_LastMember_AutoTeam_AutoDeletesEmptyTeam()
    {
        await _fx.ResetAsync();
        var bossId = await Seed.BossAsync(_fx.ConnectionString);
        var teamId = await Seed.TeamSlotAsync(_fx.ConnectionString, bossId, TeamSlotSource.Auto);
        var charId = await Seed.OccupiedSlotAsync(_fx.ConnectionString, teamId);

        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());
        await repo.DeleteCharacterAsync(new TeamSlotCharacter { Id = charId, TeamSlotId = teamId, DiscordName = "", Job = "" });

        // auto 隊移除最後成員 → 空隊自動清除
        Assert.Equal(0, await Seed.CountTeamAsync(_fx.ConnectionString, teamId));
    }

    [Fact]
    public async Task DeleteCharacter_LastMember_AdminTeam_KeepsEmptyTeam()
    {
        await _fx.ResetAsync();
        var bossId = await Seed.BossAsync(_fx.ConnectionString);
        var teamId = await Seed.TeamSlotAsync(_fx.ConnectionString, bossId, TeamSlotSource.Admin);
        var charId = await Seed.OccupiedSlotAsync(_fx.ConnectionString, teamId);

        var repo = new TeamSlotCharacterRepository(_fx.CreateDbContext());
        await repo.DeleteCharacterAsync(new TeamSlotCharacter { Id = charId, TeamSlotId = teamId, DiscordName = "", Job = "" });

        // admin 手動開的隊即使變空也保留
        Assert.Equal(1, await Seed.CountTeamAsync(_fx.ConnectionString, teamId));
    }
}
