namespace Domain.Repositories;

/// <summary>
/// 序列化同一 period 的自動排隊。
/// 兩人同時報名時，各自的「讀現有隊 → 沒有就開新隊」是 read-then-write race
/// （READ COMMITTED 下互相看不到對方未提交的隊）→ 會開出重複隊伍。
/// 取交易級 advisory lock 讓同一 period 的排隊序列化，第二個就看得到第一個建的隊。
/// </summary>
public interface IRegistrationLock
{
    /// <summary>取得該 period 的自動排隊鎖；隨 UoW 交易 commit/rollback 自動釋放。</summary>
    Task AcquireAutoAssignLockAsync(int periodId);
}
