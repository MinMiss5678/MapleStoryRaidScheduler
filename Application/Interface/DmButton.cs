namespace Application.Interface;

/// <summary>
/// DM 上一顆可點的按鈕（discord-inline-actions）。Application 層抽象——不外洩 DSharpPlus 型別；
/// 由 Infrastructure 的 DiscordService 對應成 DiscordButtonComponent。
/// </summary>
/// <param name="CustomId">點擊時 Discord 回傳的識別字串（如 <c>inv:accept:123</c>），bot 互動 handler 據此分派。</param>
/// <param name="Label">按鈕顯示文字。</param>
/// <param name="Style">按鈕樣式（顏色語意）。</param>
public record DmButton(string CustomId, string Label, DmButtonStyle Style);

/// <summary>按鈕樣式（對應 Discord 的 button style）。</summary>
public enum DmButtonStyle
{
    Primary,
    Secondary,
    Success,
    Danger
}
