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
    private static TeamSlotCharacter Member(string? characterId, bool isManual = false) =>
        new() { CharacterId = characterId, IsManual = isManual, DiscordName = "", Job = "" };

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
}
