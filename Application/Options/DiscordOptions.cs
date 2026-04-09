namespace Application.Options;

public class DiscordOptions
{
    public string BotToken { get; set; } = string.Empty;
    public string BotTokenFile { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string ClientSecretFile { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
}