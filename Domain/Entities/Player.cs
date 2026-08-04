namespace Domain.Entities;

// DataAnnotation 對 Dapper 實體無作用（見 Character.cs 說明），故不放 [MaxLength]。
public class Player
{
    public ulong DiscordId { get; set; }
    public required string DiscordName { get; set; }
    public required string Role { get; set; }
}
