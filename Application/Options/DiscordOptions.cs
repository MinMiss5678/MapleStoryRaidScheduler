namespace Application.Options;

public class DiscordOptions
{
    public string BotToken { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string GuildId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string AdminRoleId { get; set; } = string.Empty;
    public string UserRoleId { get; set; } = string.Empty;
}