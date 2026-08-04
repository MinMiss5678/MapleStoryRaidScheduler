using Application.DTOs;
using Application.Interface;
using Application.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Presentation.WebApi.Controller;

/// <summary>
/// Seq Alert 觸發時打這支端點寄送通知信（見 plans/2026-07-31-error-alerting.md）。
/// 呼叫方是 Seq 的 Seq.App.HttpRequest，不是登入使用者，故用共用密鑰保護（X-Alert-Secret），
/// 不走一般的 JWT/Session 認證流程；IdempotencyMiddleware 對 /api/internal/ 路徑一律放行
/// （見該 middleware 的路徑排除），不用帶 X-Idempotency-Key。
/// </summary>
[ApiController]
[Route("api/internal/alert-mail")]
[AllowAnonymous]
public class AlertMailController : ControllerBase
{
    private readonly IMicrosoftMailService _mailService;
    private readonly MicrosoftMailOptions _options;

    public AlertMailController(IMicrosoftMailService mailService, IOptions<MicrosoftMailOptions> options)
    {
        _mailService = mailService;
        _options = options.Value;
    }

    [HttpPost]
    public async Task<IActionResult> SendAsync(
        [FromBody] AlertMailRequest request,
        [FromHeader(Name = "X-Alert-Secret")] string? secret)
    {
        if (string.IsNullOrEmpty(_options.WebhookSecret) || secret != _options.WebhookSecret)
            return Unauthorized();

        await _mailService.SendMailAsync(request.Subject, request.Body);
        return Ok();
    }
}
