using System.Data;
using Application.Interface;
using Dapper;
using Utils.SqlBuilder;

namespace Infrastructure.Dapper;

public class DbContext
{
    public IDbConnection Connection { get; }
    public IDbTransaction? Transaction { get; private set; }
    private bool _completed;
    // 登記「交易 Commit 成功後」才執行的動作（如發行程內事件 / 通知）；Rollback 則丟棄不執行
    private readonly List<Action> _afterCommitActions = new();

    public DbContext(IDbConnection connection)
    {
        Connection = connection;
    }

    /// <summary>
    /// 登記一個「交易成功 Commit 後」才執行的副作用（設定變更事件、通知等）。
    /// 用途：避免在交易尚未提交（甚至將 Rollback）時就搶先觸發副作用——那會讓訂閱者
    /// 讀到未提交的狀態、或對根本沒生效的變更反應。Rollback 時登記的動作會被丟棄。
    /// </summary>
    public void AfterCommit(Action action) => _afterCommitActions.Add(action);

    public void Begin()
    {
        _completed = false;
        // 連線改為延遲開啟（工廠不再 eager Open）→ 開交易前先確保連線已開
        if (Connection.State != ConnectionState.Open)
            Connection.Open();
        if (Transaction == null)
            Transaction = Connection.BeginTransaction();
    }

    public void Commit()
    {
        Transaction?.Commit();
        Transaction = null;
        _completed = true;
        // 交易「確定提交」後才觸發登記的副作用——不對未提交/將回滾的狀態搶跑
        var actions = _afterCommitActions.ToArray();
        _afterCommitActions.Clear();
        foreach (var action in actions)
            action();
    }

    public void Rollback()
    {
        Transaction?.Rollback();
        Transaction = null;
        _completed = true;
        _afterCommitActions.Clear(); // 回滾 → 登記的 commit 後動作丟棄、不執行
    }

    private void EnsureNotCompleted()
    {
        if (_completed)
            throw new InvalidOperationException("Transaction has already been committed or rolled back. No further operations are allowed.");
    }

    public virtual async Task<int> ExecuteAsync<T>(SqlCommandBuilder<T> builder)
    {
        EnsureNotCompleted();
        var (sql, param) = builder.Build();
        return await Connection.ExecuteAsync(sql, param, Transaction);
    }

    public virtual async Task<int> ExecuteAsync(string sql, object param)
    {
        EnsureNotCompleted();
        return await Connection.ExecuteAsync(sql, param, Transaction);
    }

    public virtual async Task<int> ExecuteScalarAsync<T>(SqlCommandBuilder<T> builder)
    {
        EnsureNotCompleted();
        var (sql, param) = builder.Build();
        return await Connection.ExecuteScalarAsync<int>(sql, param, Transaction);
    }

    public virtual async Task<IEnumerable<TResult>> QueryAsync<TResult>(string sql, object param)
    {
        return await Connection.QueryAsync<TResult>(sql, param, Transaction);
    }

    public virtual async Task<TResult> QuerySingleAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.QuerySingleAsync<TResult>(sql, param, Transaction);
    }

    public virtual async Task<TResult?> QuerySingleOrDefaultAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.QuerySingleOrDefaultAsync<TResult?>(sql, param, Transaction);
    }

    public virtual async Task<IEnumerable<TResult>> QueryAsync<TResult>(QueryBuilder builder)
    {
        var (sql, param) = builder.Build();
        return await Connection.QueryAsync<TResult>(sql, param, Transaction);
    }

    public virtual IRepository<T> Repository<T>() where T : class
    {
        return new DapperRepository<T>(Connection, Transaction);
    }
}
