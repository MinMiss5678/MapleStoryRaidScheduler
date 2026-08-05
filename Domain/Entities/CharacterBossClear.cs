namespace Domain.Entities;

/// <summary>
/// 角色對某王的通關次數（玩家自填，同 AttackPower 信任模型、後端不查證）。
/// 派生「本王總通關次數」= 同一隻王、跨該玩家不同角色的 ClearCount 相加。見計畫 §3 / §9.14。
/// </summary>
public class CharacterBossClear
{
    public int Id { get; set; }
    public required string CharacterId { get; set; }
    public int BossId { get; set; }
    public int ClearCount { get; set; }
}
