namespace Application.Interface;

/// <summary>
/// Outbox 事件的投遞 handler。dispatcher 依 <see cref="Type"/> 把已提交的 outbox 列派給對應 handler。
/// <b>必須冪等</b>：投遞是 at-least-once（dispatcher 在「投遞成功、標 processed 之前」崩會重送），
/// 所以同一則被處理兩次不能有副作用。
/// </summary>
public interface IOutboxHandler
{
    /// <summary>對應 outbox 列的 Type 欄位。</summary>
    string Type { get; }

    /// <param name="payload">outbox 列的 JSON payload（原字串，handler 自行決定要不要解析）。</param>
    Task HandleAsync(string payload, CancellationToken cancellationToken);
}
