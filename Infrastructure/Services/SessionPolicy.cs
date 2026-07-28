namespace Infrastructure.Services;

/// <summary>
/// Session 授權政策（我自己的，與 Discord token TTL 無關）。集中於此供建立與 sliding 共用。
/// </summary>
internal static class SessionPolicy
{
    /// <summary>有效期長度：建立時與每次延展都設成 now + 此值。</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    /// <summary>節流門檻：剩餘 &lt; 此值時，活動才延展有效期（過半才續）→ 避免「每讀必寫」打架讀穿快取。</summary>
    public static readonly TimeSpan SlideThreshold = TimeSpan.FromDays(15);

    /// <summary>快取新鮮度（純加速 + 撤銷殘留上界），與有效期無關的短固定 TTL。</summary>
    public static readonly TimeSpan CacheFreshness = TimeSpan.FromMinutes(15);
}
