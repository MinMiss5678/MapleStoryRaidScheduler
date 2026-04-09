using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Entities;

namespace Infrastructure.Entities;

[Table("PlayerRegister")]
public class PlayerRegisterDbModel
{
    [Key]
    public int Id { get; set; }
    public long DiscordId { get; set; }
    public int PeriodId { get; set; }
}