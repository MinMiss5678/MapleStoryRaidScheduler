using Domain.Entities;

namespace Domain.Helpers;

/// <summary>
/// 組隊職業名額可行性（composition-quota，見 plans/2026-08-25-composition-quota.md）。
///
/// 把「名額」展開成節點：每列需求 <see cref="TeamSlotRequirement.Count"/> 個名額（只收 job ∈ 該列 Jobs）
/// + 未指定池 <c>capacity − ΣCount</c> 個名額（任意職業）。一組已確認職業「可行」＝存在一組匹配，
/// 把**每位**成員各配到一個相容名額（最大二分匹配數 == 成員數）。用 Kuhn 增廣路徑；規模小（≤ capacity）故 cheap。
///
/// OR 群組（「箭神 or 槍神 1 位」）與重疊（同職業跨多列）皆由匹配自然處理；
/// 容量溢出（成員數 > 名額數）也回不可行 → 同一函式涵蓋「職業名額滿」與「隊伍額滿」。
/// 無需求列 → 全為未指定名額 → 只受容量限制（沿用純容量把關的舊行為）。
/// </summary>
public static class CompositionQuota
{
    public static bool IsFeasible(
        IReadOnlyCollection<string> confirmedJobs,
        IReadOnlyCollection<TeamSlotRequirement> requirements,
        int capacity)
    {
        // 名額清單：null = 未指定（任意職業）；非 null = 只收該集合內的職業。
        var slots = new List<HashSet<string>?>();
        var specified = 0;
        foreach (var r in requirements)
        {
            var jobs = r.Jobs.Select(j => j.Job).ToHashSet();
            for (var i = 0; i < r.Count; i++)
                slots.Add(jobs);
            specified += r.Count;
        }
        var wildcard = Math.Max(0, capacity - specified);
        for (var i = 0; i < wildcard; i++)
            slots.Add(null);

        var members = confirmedJobs.ToList();
        if (members.Count > slots.Count)
            return false;   // 成員多於名額（含容量溢出）→ 不可行

        // Kuhn：每位成員找一條增廣路徑指派到相容名額；全部指派成功才可行。
        var matchedMember = new int[slots.Count];
        Array.Fill(matchedMember, -1);
        for (var m = 0; m < members.Count; m++)
        {
            var visited = new bool[slots.Count];
            if (!TryAssign(m, members, slots, matchedMember, visited))
                return false;
        }
        return true;
    }

    private static bool TryAssign(int member, List<string> members, List<HashSet<string>?> slots, int[] matchedMember, bool[] visited)
    {
        for (var s = 0; s < slots.Count; s++)
        {
            if (visited[s])
                continue;
            var accept = slots[s];
            if (accept is not null && !accept.Contains(members[member]))
                continue;   // 該名額不收此職業
            visited[s] = true;
            if (matchedMember[s] == -1 || TryAssign(matchedMember[s], members, slots, matchedMember, visited))
            {
                matchedMember[s] = member;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 供給覆蓋（招募熱力圖 leader-recruitment-heatmap）：給一組「可用候選職業」，最多能填滿多少「需求名額」
    /// （各需求列 <see cref="TeamSlotRequirement.Count"/> 個名額、只收其 Jobs）。與 <see cref="IsFeasible"/> 同一套二分匹配，
    /// 但方向反過來（候選 → 需求名額）、且**無未指定池**（只問覆蓋『指定的需求』）。回最大匹配數；呼叫端除以 ΣCount 得填滿比例。
    /// </summary>
    public static int MaxRequirementSlotsFilled(
        IReadOnlyCollection<string> availableJobs,
        IReadOnlyCollection<TeamSlotRequirement> requirements)
    {
        var slots = new List<HashSet<string>>();
        foreach (var r in requirements)
        {
            var jobs = r.Jobs.Select(j => j.Job).ToHashSet();
            for (var i = 0; i < r.Count; i++)
                slots.Add(jobs);
        }
        if (slots.Count == 0)
            return 0;

        var candidates = availableJobs.ToList();
        var matchedSlot = new int[slots.Count];
        Array.Fill(matchedSlot, -1);   // slot → 候選 index
        var filled = 0;
        for (var c = 0; c < candidates.Count; c++)
        {
            var visited = new bool[slots.Count];
            if (TryFill(c, candidates, slots, matchedSlot, visited))
                filled++;
        }
        return filled;
    }

    private static bool TryFill(int cand, List<string> candidates, List<HashSet<string>> slots, int[] matchedSlot, bool[] visited)
    {
        for (var s = 0; s < slots.Count; s++)
        {
            if (visited[s] || !slots[s].Contains(candidates[cand]))
                continue;
            visited[s] = true;
            if (matchedSlot[s] == -1 || TryFill(matchedSlot[s], candidates, slots, matchedSlot, visited))
            {
                matchedSlot[s] = cand;
                return true;
            }
        }
        return false;
    }
}
