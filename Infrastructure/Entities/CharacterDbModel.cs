using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("Character")]
public class CharacterDbModel
{
    [ExplicitKey]
    public required string Id { get; set; }

    public long DiscordId { get; set; }

    public required string Name { get; set; }
    public required string Job { get; set; }
    public int AttackPower { get; set; }
    public int Level { get; set; }            // 人物等級（自填，migration 000023）
    public bool IsSeekingRaid { get; set; }   // period-less §8 Phase 2：參戰 opt-in
}
