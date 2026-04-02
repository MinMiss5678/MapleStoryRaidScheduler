using Application.Interface;

namespace Infrastructure.Dapper;

public class UnitOfWork : IUnitOfWork
{
    private readonly DbContext _context;

    public UnitOfWork(DbContext context)
    {
        _context = context;
    }

    public Task BeginAsync()
    {
        _context.Begin();
        return Task.CompletedTask;
    }

    public Task CommitAsync()
    {
        _context.Commit();
        return Task.CompletedTask;
    }

    public Task RollbackAsync()
    {
        _context.Rollback();
        return Task.CompletedTask;
    }
}