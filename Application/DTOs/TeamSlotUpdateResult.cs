namespace Application.DTOs;

/// <summary>
/// UpdateAsync 的結果：哪些隊伍因為隊伍消失（被 merge/自動排團砍掉重灌）或樂觀鎖版本衝突而被略過。
/// 未列在此清單的隊伍皆已成功存檔；被略過的隊伍不會中斷其他隊伍的處理。
/// </summary>
public class TeamSlotUpdateResult
{
    public List<int> ConflictedTeamSlotIds { get; set; } = new();
}
