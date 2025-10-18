namespace Utils.SqlBuilder;

public class DbExecutor
{
    public async Task<Result<T>> ExecuteAsync<T>(Func<Task<T>> func)
    {
        try
        {
            var data = await func();
            return Result<T>.Success(data);
        }
        catch (Exception ex)
        {
            return Result<T>.Fail(ex.Message);
        }
    }
}