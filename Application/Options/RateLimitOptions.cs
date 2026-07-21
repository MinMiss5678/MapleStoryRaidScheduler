namespace Application.Options;

/// <summary>
/// 登入後按身分限流的參數（綁 appsettings 的 "RateLimit" 區段）。
/// 未設定時用預設值：每個使用者 100 次 / 10 秒。
/// </summary>
public class RateLimitOptions
{
    public int PermitLimit { get; set; } = 100;
    public int WindowSeconds { get; set; } = 10;
}
