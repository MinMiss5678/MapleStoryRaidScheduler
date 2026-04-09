using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("PlayerAvailability")]
public class PlayerAvailabilityDbModel
{
    [Key]
    public int Id { get; set; }
    public int PlayerRegisterId { get; set; }
    public int Weekday { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
}