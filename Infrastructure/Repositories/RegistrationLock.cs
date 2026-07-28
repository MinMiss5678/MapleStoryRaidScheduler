using Domain.Repositories;
using Infrastructure.Dapper;

namespace Infrastructure.Repositories;

public class RegistrationLock : IRegistrationLock
{
    // advisory lock 的命名空間 classId（避免與其他 advisory lock 撞號）；objId 用 periodId / teamSlotId。
    private const int AutoAssignLockClass = 1001;
    private const int TeamSlotEditLockClass = 1002;

    private readonly DbContext _dbContext;

    public RegistrationLock(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AcquireAutoAssignLockAsync(int periodId)
    {
        // pg_advisory_xact_lock：交易級鎖，隨 UoW 交易結束自動釋放（不必手動 unlock）。
        // 同一 (classId, periodId) 的併發交易序列化；不同 period 用不同 objId → 互不阻塞。
        // 必須跑在 UoW 的同一連線/交易上（DbContext 為 Scoped，天然同一條）。
        await _dbContext.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(@classId, @periodId)",
            new { classId = AutoAssignLockClass, periodId });
    }

    public async Task AcquireTeamSlotEditLockAsync(int teamSlotId)
    {
        // 同一 (classId, teamSlotId) 的併發編輯序列化；不同隊伍互不阻塞。
        await _dbContext.ExecuteAsync(
            "SELECT pg_advisory_xact_lock(@classId, @teamSlotId)",
            new { classId = TeamSlotEditLockClass, teamSlotId });
    }
}
