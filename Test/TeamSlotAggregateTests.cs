using Domain.Entities;
using Domain.Exceptions;
using Xunit;

namespace Test;

/// <summary>
/// TeamSlot 充血聚合的不變式：不超員、不重複、空位不計數、保護手動成員。
/// 純 domain（無 mock、無 I/O）——這些規則的權威定義在此。
/// </summary>
public class TeamSlotAggregateTests
{
    private static TeamSlotCharacter Member(string? characterId, bool isManual = false, ulong discordId = 0) =>
        new() { CharacterId = characterId, IsManual = isManual, DiscordId = discordId, DiscordName = "", Job = "" };

    [Fact]
    public void FilledCount_IgnoresEmptySlots()
    {
        var slot = new TeamSlot { Capacity = 6 };
        slot.Characters.Add(Member("c1"));
        slot.Characters.Add(Member(null));   // 空位

        Assert.Equal(1, slot.FilledCount);
    }

    [Fact]
    public void HasRoom_False_WhenFull()
    {
        var slot = new TeamSlot { Capacity = 2 };
        slot.AddMember(Member("c1"));
        slot.AddMember(Member("c2"));

        Assert.False(slot.HasRoom);
    }

    [Fact]
    public void AddMember_Throws_WhenFull()
    {
        var slot = new TeamSlot { Capacity = 1 };
        slot.AddMember(Member("c1"));

        Assert.Throws<DomainException>(() => slot.AddMember(Member("c2")));
    }

    [Fact]
    public void AddMember_Throws_WhenDuplicateCharacter()
    {
        var slot = new TeamSlot { Capacity = 6 };
        slot.AddMember(Member("c1"));

        Assert.Throws<DomainException>(() => slot.AddMember(Member("c1")));
    }

    [Fact]
    public void ReschedulableMembers_ExcludesManualAndEmpty()
    {
        var slot = new TeamSlot { Capacity = 6 };
        slot.AddMember(Member("c1"));                 // 可重排
        slot.AddMember(Member("c2", isManual: true)); // 手動，受保護
        slot.Characters.Add(Member(null));            // 空位

        var reschedulable = slot.ReschedulableMembers().ToList();

        Assert.Single(reschedulable);
        Assert.Equal("c1", reschedulable[0].CharacterId);
    }

    [Fact]
    public void SetRoster_Throws_WhenOverCapacity()
    {
        var slot = new TeamSlot { Capacity = 1 };
        var roster = new List<TeamSlotCharacter> { Member("c1", discordId: 1), Member("c2", discordId: 2) };

        Assert.Throws<DomainException>(() => slot.SetRoster(roster, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetRoster_Throws_WhenDuplicateDiscordId()
    {
        var slot = new TeamSlot { Capacity = 6 };
        var roster = new List<TeamSlotCharacter> { Member("c1", discordId: 9), Member("c2", discordId: 9) };

        Assert.Throws<DomainException>(() => slot.SetRoster(roster, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void SetRoster_ReplacesCharacters_AndStampsTeamSlotIdAndDateTime()
    {
        var slot = new TeamSlot { Id = 42, Capacity = 6 };
        slot.Characters.Add(Member("old"));
        var newDateTime = new DateTimeOffset(2026, 4, 2, 20, 0, 0, TimeSpan.Zero);
        var roster = new List<TeamSlotCharacter> { Member("c1", discordId: 1) };

        slot.SetRoster(roster, newDateTime);

        Assert.Single(slot.Characters);
        Assert.Equal("c1", slot.Characters[0].CharacterId);
        Assert.Equal(42, slot.Characters[0].TeamSlotId);
        Assert.Equal(newDateTime, slot.SlotDateTime);
    }

    [Fact]
    public void AbsorbMembers_FillsEmptySlotFirst_BeforeAppending()
    {
        var slot = new TeamSlot { Id = 7, Capacity = 6 };
        slot.Characters.Add(Member(null)); // 既有空位

        slot.AbsorbMembers(new[] { Member("c1", discordId: 1) }, DateTimeOffset.UtcNow);

        Assert.Single(slot.Characters);   // 佔用空位，不是新增一列
        Assert.Equal("c1", slot.Characters[0].CharacterId);
    }

    [Fact]
    public void AbsorbMembers_Appends_WhenNoEmptySlot()
    {
        var slot = new TeamSlot { Id = 7, Capacity = 6 };
        slot.AddMember(Member("c1", discordId: 1));

        slot.AbsorbMembers(new[] { Member("c2", discordId: 2) }, DateTimeOffset.UtcNow);

        Assert.Equal(2, slot.Characters.Count);
        Assert.Contains(slot.Characters, c => c.CharacterId == "c2");
    }

    [Fact]
    public void AbsorbMembers_Throws_WhenFull()
    {
        var slot = new TeamSlot { Capacity = 1 };
        slot.AddMember(Member("c1", discordId: 1));

        Assert.Throws<DomainException>(() =>
            slot.AbsorbMembers(new[] { Member("c2", discordId: 2) }, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AbsorbMembers_Throws_WhenDuplicateCharacter()
    {
        var slot = new TeamSlot { Capacity = 6 };
        slot.AddMember(Member("c1", discordId: 1));

        Assert.Throws<DomainException>(() =>
            slot.AbsorbMembers(new[] { Member("c1", discordId: 2) }, DateTimeOffset.UtcNow));
    }
}
