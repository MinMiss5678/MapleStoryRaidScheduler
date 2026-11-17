using System.Diagnostics;
using Domain.Exceptions;
using Domain.Repositories;
using Infrastructure.Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Repositories;

public class TeamSlotEditLock : ITeamSlotEditLock
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

    // 壓測用:量「等鎖時間」(pg_advisory_xact_lock 從發出到取得)。optional logger → 不動 DI/測試建構子;
    // 部署時 DI 會注入真 logger,advisory_lock_wait_ms 進 Serilog(Console + Seq),再讀分布定 lock_timeout。
    private readonly ILogger<TeamSlotEditLock>? _logger;

    public TeamSlotEditLock(DbContext dbContext, string lockTimeout = "5s", ILogger<TeamSlotEditLock>? logger = null)
    {
        _dbContext = dbContext;
        _lockTimeout = lockTimeout;
        _logger = logger;
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
        // 交易級鎖只在交易內有意義：沒有交易時那條 SQL 自動提交、鎖立刻釋放，SET LOCAL 也只發 WARNING——
        // 防超收的互斥會默默失效。誰漏開交易，第一次呼叫就要報錯。
        if (_dbContext.Transaction == null)
            throw new InvalidOperationException(
                "TeamSlot 編輯鎖（pg_advisory_xact_lock）必須在交易內取得：目前沒有進行中的交易，鎖會立刻釋放。");

        // SET LOCAL 只在當前交易內生效，交易結束（commit/rollback）自動還原，不會外洩到下一個請求。
        await _dbContext.ExecuteAsync($"SET LOCAL lock_timeout = '{_lockTimeout}'", new { });

        try
        {
            var sw = Stopwatch.StartNew();
            await _dbContext.ExecuteAsync(
                "SELECT pg_advisory_xact_lock(@classId, @objId)",
                new { classId, objId });
            sw.Stop();
            // 這段＝「發出取鎖 → 拿到鎖」的真實等鎖時間（不含進交易前的連線池/Kestrel 排隊）→ 拿來定 lock_timeout。
            _logger?.LogInformation("advisory_lock_wait_ms={Ms} classId={ClassId} objId={ObjId}",
                sw.ElapsedMilliseconds, classId, objId);
        }
        catch (PostgresException ex) when (ex.SqlState == LockNotAvailableSqlState)
        {
            throw new AdvisoryLockTimeoutException(
                $"取得 advisory lock 逾時（classId={classId}, objId={objId}, timeout={_lockTimeout}），持鎖方可能異常卡住。");
        }
    }
}
