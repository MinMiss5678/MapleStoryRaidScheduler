using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

/// <summary>Pull：隊長邀請某候選角色（LeaderDiscordId 由 Controller 從登入身分注入）。</summary>
public class InviteMemberRequest
{
    [Required]
    public required string CharacterId { get; set; }
}

/// <summary>玩家對收到的邀請的回應：accept（→Confirmed）/ decline（→Rejected）。</summary>
public class InvitationActionRequest
{
    [Required]
    public required string Action { get; set; }  // "accept" | "decline"
}

/// <summary>Push：玩家申請入隊（用本人某角色）。</summary>
public class ApplyRequest
{
    [Required]
    public required string CharacterId { get; set; }
}

/// <summary>Push：隊長對申請的回應：approve（→Confirmed）/ reject（→Rejected）。</summary>
public class ApplicationActionRequest
{
    [Required]
    public required string Action { get; set; }  // "approve" | "reject"
}

/// <summary>隊長轉讓提議：目標為本隊某 Confirmed 成員的 memberId（不用 raw discordId）。</summary>
public class TransferLeaderRequest
{
    [Range(1, int.MaxValue)]
    public int MemberId { get; set; }
}

/// <summary>被指定者回應隊長轉讓：accept / decline。</summary>
public class TransferLeaderActionRequest
{
    [Required]
    public required string Action { get; set; }  // "accept" | "decline"
}
