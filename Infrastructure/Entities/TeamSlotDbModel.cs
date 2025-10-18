using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("TeamSlot")]
public class TeamSlotDbModel
{
    [Key] 
    public int Id { get; set; }
    public int BossId { get; set; }
    public DateTimeOffset SlotDateTime { get; set; }
}