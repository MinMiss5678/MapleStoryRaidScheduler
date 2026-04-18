using Microsoft.AspNetCore.Mvc;

namespace Presentation.WebApi.Extensions;

public static class ControllerBaseExtensions
{
    private const string DiscordIdClaimType = "discordId";

    /// <summary>
    /// 從目前使用者的 Claims 取得 Discord ID。
    /// 若 Claim 不存在（未驗證），discordId 設為 0 並回傳 false。
    /// </summary>
    public static bool TryGetCurrentDiscordId(this ControllerBase controller, out ulong discordId)
    {
        var value = controller.User.Claims.FirstOrDefault(c => c.Type == DiscordIdClaimType)?.Value;
        if (value == null)
        {
            discordId = 0;
            return false;
        }

        discordId = Convert.ToUInt64(value);
        return true;
    }
}
