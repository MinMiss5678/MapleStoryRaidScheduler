using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Infrastructure.Entities;

[Table("SystemConfig")]
public class SystemConfigDbModel
{
    [Key]
    public int Id { get; set; }
    public bool LeaveRateWarnEnabled { get; set; }
    public int LeaveRateWindowMonths { get; set; }
    public int LeaveRateThreshold { get; set; }
    public int LeaveRateMinSample { get; set; }
}
