namespace Application.Interface;

/// <summary>
/// handler 判定「這則已失效、不該送」（例：邀請已撤、隊已滿）時丟出。
/// dispatcher 視為正常結案：標完成、LastError 記 <c>skipped: 原因</c>、不重試、不警示。
/// </summary>
public class OutboxDeliverySkippedException : Exception
{
    public OutboxDeliverySkippedException(string reason) : base(reason) { }
}
