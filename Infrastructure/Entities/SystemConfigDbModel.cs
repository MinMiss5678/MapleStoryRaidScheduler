using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("SystemConfig")]
public class SystemConfigDbModel
{
    [Key]
    public int Id { get; set; }
    public int DeadlineDayOfWeek { get; set; }
    public TimeSpan DeadlineTime { get; set; }
    public DateTimeOffset RegistrationDeadline { get; set; }
    public bool IsDeadlineNotified { get; set; }
}
