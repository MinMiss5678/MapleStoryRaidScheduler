namespace Application.Interface;

/// <summary>
/// DM 上一個 embed（bot-composed-embeds）。Application 層抽象——不外洩 DSharpPlus；
/// 由 Infrastructure 的 DiscordService 對應成 DiscordEmbedBuilder。
/// </summary>
/// <param name="Title">標題（如「隊長邀請你加入「王」時段」）。</param>
/// <param name="Description">內文（如目前成員「職業 攻擊力」多行清單）。</param>
/// <param name="Footer">頁尾（如缺額），可 null。</param>
public record DmEmbed(string Title, string Description, string? Footer);
