namespace Domain.Entities;

/// <summary>
/// 角色的偏好王（複選）：候選匹配的**軟訊號**來源。純多對多、無額外屬性。
/// 用於隊長挑候選時「偏好本王」排前 + 標記，**不做硬篩**（守 boss-agnostic，見計畫 2026-08-24-preferred-boss-candidate-signal）。
/// </summary>
public class CharacterPreferredBoss
{
    public required string CharacterId { get; set; }
    public int BossId { get; set; }
}
