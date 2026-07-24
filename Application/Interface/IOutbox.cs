namespace Application.Interface;

/// <summary>
/// Transactional outbox 的寫入端：把「要投遞的副作用」寫進**當前 UnitOfWork 的同一筆交易**。
/// 因此 outbox 列與業務資料一起 commit / 一起 rollback（原子）——請求回滾就不會有鬼影事件。
/// 實作用 <c>DbContext</c> 的交易，故必須在寫入請求（有開交易）內呼叫。
/// </summary>
public interface IOutbox
{
    /// <param name="type">事件類型，對應 <see cref="IOutboxHandler.Type"/>。</param>
    /// <param name="payload">事件內容，序列化成 JSON 存入（自描述、可稽核）。</param>
    Task EnqueueAsync(string type, object payload);
}
