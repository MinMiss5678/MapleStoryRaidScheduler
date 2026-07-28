using Domain.Exceptions;

namespace Domain.Entities;

public class TeamSlot
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public int PeriodId { get; set; }
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public List<TeamSlotCharacter> Characters { get; set; } = new();
    public string Source { get; set; } = TeamSlotSource.Auto;        // auto | admin，見 TeamSlotSource
    public int? TemplateId { get; set; }

    // 容量 = Boss.RequireMembers（由 service 載入/建立時填；不變式需要它才守得住）
    public int Capacity { get; set; }

    // ── 隊伍不變式（充血聚合：TeamSlot 自己保證不超員/不重複/保護手動成員）──
    // 只維護記憶體物件圖；持久化仍由 service 做（命令式 Dapper，無 change-tracking）。

    /// <summary>已填成員數（空位 CharacterId == null 不算）。</summary>
    public int FilledCount => Characters.Count(c => c.CharacterId != null);

    /// <summary>還有空間可加人。</summary>
    public bool HasRoom => FilledCount < Capacity;

    /// <summary>此角色是否已在隊上。</summary>
    public bool Contains(string characterId) => Characters.Any(c => c.CharacterId == characterId);

    /// <summary>批次重排/合併「可覆蓋」的成員——IsManual 受保護、空位除外。</summary>
    public IEnumerable<TeamSlotCharacter> ReschedulableMembers()
        => Characters.Where(c => !c.IsManual && c.CharacterId != null);

    /// <summary>
    /// 加入成員（append）：擋重複、擋超額。對應 service 的 INSERT 新列，故此處 append、不填既有空位
    /// （填空位是 merge/範本的 UPDATE 語意，另案處理）。違反不變式丟 <see cref="DomainException"/>。
    /// </summary>
    public void AddMember(TeamSlotCharacter member)
    {
        if (member.CharacterId != null && Contains(member.CharacterId))
            throw new DomainException($"角色 {member.CharacterId} 已在此隊");
        if (!HasRoom)
            throw new DomainException($"隊伍已滿（{Capacity}）");

        Characters.Add(member);
    }
}
