namespace Domain.Exceptions;

/// <summary>
/// 交易級 advisory lock（<see cref="Repositories.ITeamSlotEditLock"/>）在設定的 lock_timeout 內拿不到。
/// pg_advisory_xact_lock 預設無限期等待——若持鎖的另一筆交易異常卡住（慢查詢、連線異常未正常
/// commit/rollback），後面所有搶同一資源（同隊伍 / 同 period）的請求會跟著無限期掛住、不會報錯，
/// 使用者只看到轉圈圈。設 lock_timeout 把「正常排隊等一下」跟「對方卡死、不該再等」分開。
/// 不是 <see cref="DomainException"/>（不是業務規則違反，是基礎設施層面的資源競爭症狀）。
/// </summary>
public class AdvisoryLockTimeoutException : Exception
{
    public AdvisoryLockTimeoutException(string message) : base(message) { }
}
