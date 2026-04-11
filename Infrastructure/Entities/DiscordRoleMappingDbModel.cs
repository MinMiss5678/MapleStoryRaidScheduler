using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("DiscordRoleMapping")]
public class DiscordRoleMappingDbModel
{
    [ExplicitKey]
    public long DiscordRoleId { get; set; }
    [MaxLength(50)]
    public required string Role { get; set; }
    public int Priority { get; set; }
}