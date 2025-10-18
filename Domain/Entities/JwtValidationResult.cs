namespace Domain.Entities;

public class JwtValidationResult
{
    public ulong DiscordId { get; set; }
    public bool IsValid { get; set; }
    public Exception Exception { get; set; }
}