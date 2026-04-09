namespace Domain.Helpers;

public static class JobCategoryHelper
{
    public static bool IsInJobCategory(string job, string category, Dictionary<string, HashSet<string>> jobCategories)
    {
        if (string.IsNullOrWhiteSpace(category)) return false;
        if (category == "任意") return true;

        // 支援多個職業以逗號、斜線、空格或管線分隔
        var categories = category.Split([',', '/', ' ', '|'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var cat in categories)
        {
            if (job == cat) return true;

            if (jobCategories.TryGetValue(cat, out var jobs) && jobs.Contains(job)) return true;
        }

        return false;
    }
}
