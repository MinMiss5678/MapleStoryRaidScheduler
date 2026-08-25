using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("TeamSlotCharacter")]
public class TeamSlotCharacterDbModel
{
    [Key]
    public int? Id { get; set; }
    public int TeamSlotId { get; set; }
    public long DiscordId { get; set; }
    public required string DiscordName { get; set; }
    public string? CharacterId { get; set; }
    public string? CharacterName { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int Level { get; set; }             // 人物等級快照（migration 000023）
    public int Rounds { get; set; }
    public bool IsManual { get; set; }
    public string Status { get; set; } = "Confirmed";      // leader-led（000009）：入隊狀態機
    public DateTimeOffset? SlotDateTime { get; set; }        // leader-led（000011）：跨隊重疊 unique 用
}
