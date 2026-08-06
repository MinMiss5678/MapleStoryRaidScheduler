using Domain.Exceptions;

namespace Domain.Entities;

public class TeamSlot
{
    public int Id { get; set; }
    public int BossId { get; set; }
    public int PeriodId { get; set; }   // leader-led：週期權威歸屬（migration 000009 加真欄+回填，見計畫 §3）
    public string? BossName { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
    public List<TeamSlotCharacter> Characters { get; set; } = new();
    public string Source { get; set; } = TeamSlotSource.Auto;        // auto | admin，見 TeamSlotSource
    public int? TemplateId { get; set; }

    /// <summary>隊長歸屬（leader-led，§3）。null=未認領草稿。Phase 1a 先落地屬性，repo 映射待 1b/1c。</summary>
    public ulong? LeaderDiscordId { get; set; }

    /// <summary>隊伍說明/公告（leader-led，§3 吸收非結構化招募需求）。Phase 1a 先落地屬性，repo 映射待 1b/1c。</summary>
    public string? Description { get; set; }

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

    /// <summary>
    /// 合併：套用範本配對後的完整名單（整組覆蓋，含範本產生的空位列）。
    /// 呼叫端（merge 演算法）應已篩過容量/重複才會走到這裡；此處防禦性重驗，違反丟 <see cref="DomainException"/>。
    /// </summary>
    public void SetRoster(IReadOnlyList<TeamSlotCharacter> roster, DateTimeOffset mergedDateTime)
    {
        var filledCount = roster.Count(c => c.CharacterId != null);
        if (filledCount > Capacity)
            throw new DomainException($"合併後人數（{filledCount}）超過隊伍容量（{Capacity}）");
        if (roster.Where(c => c.CharacterId != null).GroupBy(c => c.DiscordId).Any(g => g.Count() > 1))
            throw new DomainException("合併後名單有玩家重複");

        Characters = roster.ToList();
        foreach (var c in Characters) c.TeamSlotId = Id;
        SlotDateTime = mergedDateTime;
    }

    /// <summary>
    /// 合併：無範本時吸收另一隊的成員——優先填自己的既有空位，滿了才 append。
    /// 對應 merge 現有語意（PerformMerge 的 else 分支）；違反容量/重複丟 <see cref="DomainException"/>。
    /// </summary>
    public void AbsorbMembers(IEnumerable<TeamSlotCharacter> incomingMembers, DateTimeOffset mergedDateTime)
    {
        foreach (var member in incomingMembers)
        {
            if (member.CharacterId != null && Contains(member.CharacterId))
                throw new DomainException($"角色 {member.CharacterId} 已在此隊");

            var emptySlot = Characters.FirstOrDefault(c => c.CharacterId == null);
            if (emptySlot != null)
            {
                emptySlot.DiscordId = member.DiscordId;
                emptySlot.DiscordName = member.DiscordName;
                emptySlot.CharacterId = member.CharacterId;
                emptySlot.CharacterName = member.CharacterName;
                emptySlot.Job = member.Job;
                emptySlot.AttackPower = member.AttackPower;
                emptySlot.Rounds = member.Rounds;
                emptySlot.IsManual = member.IsManual;
            }
            else
            {
                if (!HasRoom)
                    throw new DomainException($"隊伍已滿（{Capacity}）");
                member.TeamSlotId = Id;
                Characters.Add(member);
            }
        }

        SlotDateTime = mergedDateTime;
    }
}
