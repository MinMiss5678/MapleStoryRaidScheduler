using System.Net.Http.Headers;
using System.Text.Json;
using Application.DTOs;
using Application.Interface;
using Application.Options;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Infrastructure.Services;

public class DiscordOAuthClient: IDiscordOAuthClient
{
    private readonly DiscordOptions _discordOptions;
    private readonly HttpClient _http;

    public DiscordOAuthClient(IOptions<DiscordOptions> discordOptions, HttpClient http)
    {
        _discordOptions = discordOptions.Value;
        _http = http;
    }

    public async Task<DiscordTokenResponse> ExchangeCodeAsync(string code)
    {
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _discordOptions.ClientId),
            new KeyValuePair<string, string>("client_secret", _discordOptions.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("redirect_uri", _discordOptions.RedirectUri)
        });

        var resp = await _http.PostAsync("https://discord.com/api/oauth2/token", body);
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<DiscordTokenResponse>(json)!;
    }

    public async Task<DiscordUserDto> GetUserAsync(string accessToken)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var resp = await _http.GetAsync("https://discord.com/api/users/@me");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<DiscordUserDto>(json)!;
    }

    public async Task<DiscordTokenResponse?> RefreshTokenAsync(string refreshToken)
    {
        var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("client_id", _discordOptions.ClientId),
            new KeyValuePair<string, string>("client_secret", _discordOptions.ClientSecret),
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });

        var resp = await _http.PostAsync("https://discord.com/api/oauth2/token", body);
        if (!resp.IsSuccessStatusCode) return null;

        var json = await resp.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<DiscordTokenResponse>(json);
    }

    public async Task<IEnumerable<string>> GetUserRolesAsync(ulong discordId)
    {
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bot", _discordOptions.BotToken);
        var resp = await _http.GetAsync($"https://discord.com/api/guilds/{_discordOptions.GuildId}/members/{discordId}");
        resp.EnsureSuccessStatusCode();

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var roles = doc.RootElement.GetProperty("roles").EnumerateArray().Select(x => x.GetString()!).ToList();
        return roles;
    }
}