using System.ComponentModel.DataAnnotations.Schema;
using Domain.Attributes;

namespace Infrastructure.Entities;

[Table("Session")]
public class SessionDbModel
{
    [ExplicitKey]
    public string SessionId { get; set; } = "";
    public long DiscordId { get; set; }
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTimeOffset Expiry { get; set; }
}