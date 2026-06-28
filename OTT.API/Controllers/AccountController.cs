using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;

namespace OTT.API.Controllers;

/// <summary>
/// Self-service privacy endpoints (GDPR / DPDP) for the authenticated user: export my data and
/// delete my account.
/// </summary>
[ApiController]
[Route("api/account")]
[Authorize]
public class AccountController : ControllerBase
{
    private readonly IGdprService _gdpr;

    public AccountController(IGdprService gdpr) => _gdpr = gdpr;

    // Right to data portability — download everything we hold about you as JSON.
    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        var data = await _gdpr.ExportAsync(HttpContext.RequireUserId(), HttpContext.GetTenantId());
        Response.Headers.ContentDisposition = "attachment; filename=my-data.json";
        return Ok(ApiResponse<object>.Ok(data));
    }

    // Right to erasure — anonymizes PII, deletes behavioural data, revokes sessions.
    [HttpDelete]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountConfirmation body)
    {
        if (!body.Confirm)
            return BadRequest(ApiResponse<object>.Fail("Set confirm=true to permanently delete your account."));

        var ok = await _gdpr.DeleteAccountAsync(
            HttpContext.RequireUserId(),
            HttpContext.GetTenantId(),
            HttpContext.Connection.RemoteIpAddress?.ToString());

        return ok
            ? Ok(new { message = "Your account has been deleted and personal data erased." })
            : NotFound(ApiResponse<object>.Fail("Account not found."));
    }
}

public record DeleteAccountConfirmation(bool Confirm);
