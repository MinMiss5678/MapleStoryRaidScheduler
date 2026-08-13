namespace Application.DTOs;

/// <summary>玩家自填的「某角色對某王的通關次數」——輸入/輸出同形。leader-led 候選 MinClearCount 過濾的資料來源。</summary>
public class BossClearDto
{
    public int BossId { get; set; }
    public int ClearCount { get; set; }
}
