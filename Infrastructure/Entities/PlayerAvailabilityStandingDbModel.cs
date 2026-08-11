using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("PlayerAvailabilityStanding")]
public class PlayerAvailabilityStandingDbModel
{
    [Key]
    public int Id { get; set; }
    public long DiscordId { get; set; }
    public int Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}
