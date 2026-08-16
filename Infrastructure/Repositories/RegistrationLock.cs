using Domain.Exceptions;
using Domain.Repositories;
using Infrastructure.Dapper;
using Npgsql;

namespace Infrastructure.Repositories;

public class RegistrationLock : IRegistrationLock
{
    // advisory lock 的命名空間 classId（避免與其他 advisory lock 撞號）；objId 用 teamSlotId。
    // （classId 1001 的 auto-assign 鎖已隨 period-less 4d 自動排團退場，號碼保留不重用。）
    private const int TeamSlotEditLockClass = 1002;

    // Postgres SQLSTATE：等待 lock_timeout 逾時觸發（NOWAIT 也會觸發同一碼，這裡只用 timeout）。
    private const string LockNotAvailableSqlState = "55P03";

    private readonly DbContext _dbContext;

    // pg_advisory_xact_lock 預設無限期等待；設上限避免持鎖方異常卡住時，後面同資源的請求跟著無限期掛住。
    // 可注入覆寫（整合測試用短逾時驗證真實行為，不用真的等 5 秒）；不是使用者輸入 →
    // 用字串內插組 SQL 沒有注入風險（SET 系語句本身也不支援 bind 參數）。
    private readonly string _lockTimeout;

    public RegistrationLock(DbContext dbContext, string lockTimeout = "5s")
    {
        _dbContext = dbContext;
        _lockTimeout = lockTimeout;
    }

    public async Task AcquireTeamSlotEditLockAsync(int teamSlotId)
    {
        // pg_advisory_xact_lock：交易級鎖，隨 UoW 交易結束自動釋放（不必手動 unlock）。
        // 同一 (classId, teamSlotId) 的併發入隊定案序列化；不同隊伍用不同 objId → 互不阻塞。
        // 必須跑在 UoW 的同一連線/交易上（DbContext 為 Scoped，天然同一條）。
        await AcquireAsync(TeamSlotEditLockClass, teamSlotId);
    }

    private async Task AcquireAsync(int classId, int objId)
    {
        // SET LOCAL 只在當前交易內生效，交易結束（commit/rollback）自動還原，不會外洩到下一個請求。
        await _dbContext.ExecuteAsync($"SET LOCAL lock_timeout = '{_lockTimeout}'", new { });

        try
        {
            await _dbContext.ExecuteAsync(
                "SELECT pg_advisory_xact_lock(@classId, @objId)",
                new { classId, objId });
        }
        catch (PostgresException ex) when (ex.SqlState == LockNotAvailableSqlState)
        {
            throw new AdvisoryLockTimeoutException(
                $"取得 advisory lock 逾時（classId={classId}, objId={objId}, timeout={_lockTimeout}），持鎖方可能異常卡住。");
        }
    }
}
