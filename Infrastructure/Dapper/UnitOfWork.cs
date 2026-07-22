using Application.Interface;

namespace Infrastructure.Dapper;

public class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _context;

    public UnitOfWork(DbContext context)
    {
        _context = context;
    }

    // 直接委派給 DbContext 的真非同步交易方法（不再包成 Task.CompletedTask 的假 async）
    public Task BeginAsync() => _context.BeginAsync();

    public Task CommitAsync() => _context.CommitAsync();

    public Task RollbackAsync() => _context.RollbackAsync();
}
