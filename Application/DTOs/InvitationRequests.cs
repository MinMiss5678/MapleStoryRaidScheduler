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
