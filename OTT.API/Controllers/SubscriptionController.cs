using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;

namespace OTT.API.Controllers;

// Request bodies
public record CancelSubscriptionRequestDto(string? Reason);
public record ValidatePromoRequestDto(string Code, Guid PlanId);
public record GoogleIapRequestDto(string ProductId, string PurchaseToken);
public record AppleIapRequestDto(string ReceiptData);

[ApiController]
[Route("api")]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _sub;

    public SubscriptionController(ISubscriptionService sub) => _sub = sub;

    // ── Plans ──
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _sub.GetPlansAsync(HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(plans));
    }

    // ── My subscription ──
    [HttpGet("subscriptions/me")]
    [Authorize]
    public async Task<IActionResult> GetMySubscription()
    {
        var sub = await _sub.GetActiveSubscriptionAsync(HttpContext.RequireUserId());
        return Ok(ApiResponse<object?>.Ok(sub));
    }

    // ── Create order (Razorpay / Stripe) ──
    [HttpPost("subscriptions/initiate")]
    [Authorize]
    public async Task<IActionResult> Initiate([FromBody] CreateOrderDto dto)
    {
        var order = await _sub.CreateOrderAsync(dto, HttpContext.RequireUserId(), HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(order));
    }

    // ── Confirm payment ──
    [HttpPost("subscriptions/confirm")]
    [Authorize]
    public async Task<IActionResult> Confirm([FromBody] VerifyPaymentDto dto)
    {
        var result = await _sub.VerifyPaymentAsync(dto, HttpContext.RequireUserId(), HttpContext.GetTenantId());
        return Ok(ApiResponse<object>.Ok(result));
    }

    // ── Cancel (auto-renew off; access continues to period end) ──
    [HttpPost("subscriptions/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel([FromBody] CancelSubscriptionRequestDto req)
    {
        var userId = HttpContext.RequireUserId();
        var active = await _sub.GetActiveSubscriptionAsync(userId);
        if (active == null) return NotFound(new { error = "No active subscription" });

        await _sub.CancelSubscriptionAsync(active.Id, userId);
        return Ok(new { message = "Auto-renewal cancelled. Access continues until the period ends." });
    }

    // ── In-App Purchases ──
    [HttpPost("subscriptions/iap/google")]
    [Authorize]
    public async Task<IActionResult> ConfirmGoogleIap([FromBody] GoogleIapRequestDto req)
    {
        var receipt = $"{req.ProductId}:{req.PurchaseToken}";
        var ok = await _sub.VerifyIapReceiptAsync(receipt, "android", HttpContext.RequireUserId(), HttpContext.GetTenantId());
        return ok ? Ok(new { message = "Subscription activated via Google Play" })
                  : BadRequest(new { error = "Receipt verification failed" });
    }

    [HttpPost("subscriptions/iap/apple")]
    [Authorize]
    public async Task<IActionResult> ConfirmAppleIap([FromBody] AppleIapRequestDto req)
    {
        var ok = await _sub.VerifyIapReceiptAsync(req.ReceiptData, "ios", HttpContext.RequireUserId(), HttpContext.GetTenantId());
        return ok ? Ok(new { message = "Subscription activated via App Store" })
                  : BadRequest(new { error = "Receipt verification failed" });
    }

    // ── Promo validation ──
    [HttpPost("promo/validate")]
    [Authorize]
    public async Task<IActionResult> ValidatePromo([FromBody] ValidatePromoRequestDto req)
    {
        var (success, discount) = await _sub.ApplyPromoCodeAsync(req.Code, HttpContext.GetTenantId(), req.PlanId);
        if (!success) return BadRequest(new { valid = false, error = "Invalid or expired promo code" });
        return Ok(new { valid = true, discountAmount = discount });
    }

    // ── Invoices / payment history ──
    [HttpGet("invoices")]
    [Authorize]
    public async Task<IActionResult> GetInvoices()
    {
        var history = await _sub.GetPaymentHistoryAsync(HttpContext.RequireUserId());
        return Ok(ApiResponse<object>.Ok(history));
    }
}
