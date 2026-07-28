namespace Domain.Exceptions;

/// <summary>
/// 領域不變式違反（如隊伍已滿、成員重複）。丟在 Domain 層（不能依賴 Application 的 AppException）；
/// 由 Presentation 的 ExceptionHandlerMiddleware 映射為 400。
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}
