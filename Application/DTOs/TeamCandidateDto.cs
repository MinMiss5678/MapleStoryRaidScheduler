namespace Application.DTOs;

/// <summary>
/// 隊長挑人的候選（leader-led §4/§9.12）：**只回能力欄、不含 discord 身分**（按能力挑、非身分；
/// 身分於 pick/承諾後才揭露）。CharacterId 是遊戲角色代碼（非 PII），供隊長發邀請的目標。
/// </summary>
public class TeamCandidateDto
{
    public required string CharacterId { get; set; }
    public required string CharacterName { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int MapleBlessingLevel { get; set; }    // 挑 buffer 用（§9.18）
    public int BossClearCount { get; set; }         // 本王總通關（跨該玩家角色加總，老手參考，§9.14）
}
