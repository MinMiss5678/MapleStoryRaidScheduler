using System.ComponentModel.DataAnnotations;

namespace Application.DTOs;

/// <summary>
/// 招募熱力圖查詢（leader-recruitment-heatmap）：隊長在 /teams/new 設好草稿需求後，查未來 N 天各整點
/// 「這套組成的可填程度」。需求尚未建隊 → 帶草稿 <see cref="CreateTeamRequirementDto"/>。
/// </summary>
public class RecruitmentHeatmapCommand
{
    [Range(1, int.MaxValue)]
    public int BossId { get; set; }

    /// <summary>往後看幾天（1–30，預設 14）。</summary>
    [Range(1, 30)]
    public int Days { get; set; } = 14;

    public List<CreateTeamRequirementDto> Requirements { get; set; } = [];
}

/// <summary>熱力圖結果：整套需求名額數 + 各格（未來整點）的可填數。</summary>
public class RecruitmentHeatmapDto
{
    public int TotalRequired { get; set; }              // ΣCount（分母）
    public List<HeatmapCellDto> Cells { get; set; } = [];
}

/// <summary>一格：某真實日期整點開團時，合格且有空（扣不可分身）的候選能填滿多少需求名額。</summary>
public class HeatmapCellDto
{
    public DateTimeOffset SlotDateTime { get; set; }    // +8，點格帶入 /teams/new 的開團時間
    public int FilledCount { get; set; }                // 可填名額數（0..TotalRequired）；前端 /TotalRequired 上色
}
