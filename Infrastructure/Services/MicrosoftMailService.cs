using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.Interface;
using Application.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

/// <summary>
/// 用既有 refresh token（Delegated Mail.Send + offline_access，一次性登入取得）換 access token，
/// 呼叫 Microsoft Graph 寄信。見 plans/2026-07-31-error-alerting.md 的調查與決策紀錄。
/// </summary>
public class MicrosoftMailService : IMicrosoftMailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MicrosoftMailOptions _options;

    public MicrosoftMailService(IHttpClientFactory httpClientFactory, IOptions<MicrosoftMailOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public async Task SendMailAsync(string subject, string body)
    {
        var client = _httpClientFactory.CreateClient();

        // 換 access token。注意：Microsoft 可能在回應裡一併發新的 refresh token（輪替），這裡刻意不存檔
        // 儲存——低風險的已知限制，真的失效時重跑一次性登入流程換新 refresh token 即可，不影響主功能。
        var tokenResponse = await client.PostAsync(
            $"https://login.microsoftonline.com/{_options.TenantId}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _options.RefreshToken,
                ["scope"] = "https://graph.microsoft.com/Mail.Send offline_access"
            }));
        tokenResponse.EnsureSuccessStatusCode();

        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        var accessToken = JsonDocument.Parse(tokenJson).RootElement.GetProperty("access_token").GetString();

        using var mailRequest = new HttpRequestMessage(HttpMethod.Post, "https://graph.microsoft.com/v1.0/me/sendMail");
        mailRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        mailRequest.Content = JsonContent.Create(new
        {
            message = new
            {
                subject,
                body = new { contentType = "Text", content = body },
                toRecipients = new[] { new { emailAddress = new { address = _options.ToAddress } } }
            }
        });

        var sendResponse = await client.SendAsync(mailRequest);
        sendResponse.EnsureSuccessStatusCode();
    }
}
