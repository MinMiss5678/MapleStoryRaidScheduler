namespace Domain.Repositories;

/// <summary>
/// 序列化同一隊伍的入隊定案（ConfirmMember：accept/approve）。
/// 同瞬間兩個定案請求各自讀到「還有空位」的舊快照、各自通過容量檢查 → 一起寫入造成超編；
/// 取交易級 advisory lock 讓同一隊伍的定案序列化，第二個在鎖內重讀 Confirmed 數就會正確看到已滿。
/// （period-less 4d 前另有 auto-assign 鎖序列化同一 period 的自動排團，已隨自動排團退場。）
/// </summary>
public interface IRegistrationLock
{
    /// <summary>
    /// 序列化同一隊伍的入隊定案（ConfirmMember）。
    /// 防同瞬間兩請求各自讀到「還有空位」的舊快照、各自通過容量檢查 → 一起寫入造成超編。
    /// 隨 UoW 交易 commit/rollback 自動釋放。
    /// </summary>
    Task AcquireTeamSlotEditLockAsync(int teamSlotId);
}
