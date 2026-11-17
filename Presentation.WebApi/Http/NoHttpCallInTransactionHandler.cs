using Infrastructure.Dapper;

namespace Presentation.WebApi.Http;

/// <summary>
/// 交易中不准發對外 HTTP（防交易範圍太大）。網路呼叫被包進 DB 交易時，那條連線在等外部服務的期間一直被佔住、
/// 閒置在交易中；外部服務一慢，連線池就會被耗盡、全站 DB 存取卡住（例：登入時三次 Discord 呼叫）。
/// 掛在所有 <see cref="HttpClient"/> 上（見 AddNoHttpCallInTransactionGuard），送出前檢查本請求的 <see cref="DbContext"/>。
/// 限制：只涵蓋 HTTP 請求內；bot、背景工作沒有 HttpContext，不檢查。
/// </summary>
public class NoHttpCallInTransactionHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var db = httpContextAccessor.HttpContext?.RequestServices.GetService<DbContext>();
        if (db?.Transaction != null)
            throw new InvalidOperationException(
                $"不可在 DB 交易中發出對外 HTTP 請求（{request.Method} {request.RequestUri?.Host}）：" +
                "等待外部服務期間會一直佔住連線。請先完成外部呼叫，再開交易寫 DB。");

        return base.SendAsync(request, cancellationToken);
    }
}
