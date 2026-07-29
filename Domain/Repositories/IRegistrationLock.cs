namespace Domain.Repositories;

/// <summary>
/// 序列化同一 period 的自動分配。
/// 兩人同時報名時，各自的「讀現有隊 → 沒有就開新隊」是 read-then-write race
/// （READ COMMITTED 下互相看不到對方未提交的隊）→ 會開出重複隊伍。
/// 取交易級 advisory lock 讓同一 period 的自動分配序列化，第二個就看得到第一個建的隊。
/// </summary>
public interface IRegistrationLock
{
    /// <summary>取得該 period 的自動分配鎖；隨 UoW 交易 commit/rollback 自動釋放。</summary>
    Task AcquireAutoAssignLockAsync(int periodId);

    /// <summary>
    /// 序列化同一隊伍的管理員手動編輯（新增/移除成員）。
    /// 防同瞬間兩請求各自讀到「還有空位」的舊快照、各自通過容量檢查 → 一起寫入造成超編；
    /// 也讓「移除最後一人連帶砍團」與「同時新增成員」不會撞外鍵違反，序列化後第二個請求會正確讀到隊伍已消失。
    /// 隨 UoW 交易 commit/rollback 自動釋放。
    /// </summary>
    Task AcquireTeamSlotEditLockAsync(int teamSlotId);
}
