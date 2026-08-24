namespace Application.DTOs;

/// <summary>
/// 隊長挑人的候選（leader-led §4）：能力欄 + **顯示名（公會暱稱優先）**。
/// §9.12 匿名已翻（2026-08-07 透明化，見 plans/2026-08-07-leave-team-and-candidate-dedup.md）——
/// 公會內固定開團、身分本互通，改為顯示 DiscordName 讓隊長認得出老班底、養固定團。
/// raw discordId 仍留後端（邀請以 CharacterId 為目標）。
/// </summary>
public class TeamCandidateDto
{
    public required string CharacterId { get; set; }
    public required string CharacterName { get; set; }
    public string? DiscordName { get; set; }        // 顯示名（登入時 nick ?? global_name ?? username）
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int MapleBlessingLevel { get; set; }    // 挑 buffer 用（§9.18）
    public int BossClearCount { get; set; }         // 本王總通關（跨該玩家角色加總，老手參考，§9.14）
    public bool LeaveRateWarn { get; set; }         // 退團率偏高警示（Feature 1b；admin 開且達門檻才 true）
    public bool PrefersThisBoss { get; set; }       // 該角色偏好清單含本王 → 前端標「偏好此王」+ 後端排前（軟訊號，非硬篩）
}
