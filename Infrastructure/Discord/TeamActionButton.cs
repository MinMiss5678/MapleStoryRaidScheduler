namespace Infrastructure.Discord;

/// <summary>DM 內建動作的族別（discord-inline-actions）。每族一組「正向/負向」兩顆按鈕。</summary>
public enum TeamActionFamily
{
    /// <summary>邀請 → 玩家：接受 / 拒絕（走 Accept/DeclineInviteAsync）。id＝memberId。</summary>
    Invite,

    /// <summary>申請審核 → 隊長：核准 / 拒絕（走 Approve/RejectAsync）。id＝memberId。</summary>
    Application,

    /// <summary>隊長轉讓 → 新隊長候選：接受 / 拒絕（走 RespondLeaderTransferAsync）。id＝teamSlotId。</summary>
    Transfer
}

/// <summary>
/// 團隊動作按鈕 custom_id 的編/解碼，producer（outbox handler 組按鈕）與 consumer（互動 handler 解析）共用：
/// <c>{prefix}:{verb}:{id}</c>——prefix＝inv/app/xfer，verb＝該族正/負向動詞，id＝目標 Id。
/// 純函式、無相依 → 可單元測試（互動 handler 的 Parse seam）。
/// </summary>
public static class TeamActionButton
{
    public static string CustomId(TeamActionFamily family, bool positive, int id) =>
        $"{Prefix(family)}:{Verb(family, positive)}:{id}";

    /// <summary>解析 custom_id → 族別 + 正/負向 + id。格式不符回 false。</summary>
    public static bool TryParse(string customId, out TeamActionFamily family, out bool positive, out int id)
    {
        family = default;
        positive = false;
        id = 0;
        var parts = customId.Split(':');
        if (parts.Length != 3)
            return false;
        switch (parts[0])
        {
            case "inv": family = TeamActionFamily.Invite; break;
            case "app": family = TeamActionFamily.Application; break;
            case "xfer": family = TeamActionFamily.Transfer; break;
            default: return false;
        }
        var (pos, neg) = Verbs(family);
        if (parts[1] == pos) positive = true;
        else if (parts[1] == neg) positive = false;
        else return false;
        return int.TryParse(parts[2], out id);
    }

    private static string Prefix(TeamActionFamily family) => family switch
    {
        TeamActionFamily.Invite => "inv",
        TeamActionFamily.Application => "app",
        TeamActionFamily.Transfer => "xfer",
        _ => "inv"
    };

    // 申請審核用「核准/拒絕」語意（approve/reject）；邀請與轉讓用「接受/拒絕」（accept/decline）。
    private static (string positive, string negative) Verbs(TeamActionFamily family) => family switch
    {
        TeamActionFamily.Application => ("approve", "reject"),
        _ => ("accept", "decline")
    };

    private static string Verb(TeamActionFamily family, bool positive)
    {
        var (pos, neg) = Verbs(family);
        return positive ? pos : neg;
    }
}
