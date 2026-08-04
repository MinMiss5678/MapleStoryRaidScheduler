namespace Application.Options;

/// <summary>
/// Seq Alert 觸發時寄通知信用的 Microsoft Graph 設定（見 plans/2026-07-31-error-alerting.md）。
/// RefreshToken 來自一次性的 Authorization Code + PKCE 登入流程（Delegated Mail.Send + offline_access），
/// 不做 refresh token 輪替落地——這是錦上添花的通知功能，真的失效時重跑一次性登入流程即可，不影響主功能。
/// </summary>
public class MicrosoftMailOptions
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string RefreshTokenFile { get; set; } = string.Empty;
    public string ToAddress { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string WebhookSecretFile { get; set; } = string.Empty;
}
