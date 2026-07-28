using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("Session")]
public class SessionDbModel
{
    [ExplicitKey]
    public string SessionId { get; set; } = "";
    public long DiscordId { get; set; }
    public DateTimeOffset SessionExpiry { get; set; }
}
