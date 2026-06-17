using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.Application.DTOs.Subscription;
using OTT.Application.Interfaces;

namespace OTT.API.Controllers;

[ApiController]
[Route("api")]
public class SubscriptionController : ControllerBase
{
    private readonly ISubscriptionService _sub;
    private readonly IPaymentService _payment;
    private readonly INotificationService _notif;

    public SubscriptionController(ISubscriptionService sub, IPaymentService payment, INotificationService notif)
    {
        _sub = sub; _payment = payment; _notif = notif;
    }

    // ── Plans ──
    [HttpGet("plans")]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _sub.GetActivePlansAsync();
        return Ok(plans);
    }

    // ── My Subscription ──
    [HttpGet("subscriptions/me")]
    [Authorize]
    public async Task<IActionResult> GetMySubscription()
    {
        var userId = GetUserId();
        var sub = await _sub.GetUserSubscriptionAsync(userId);
        return Ok(sub);
    }

    // ── Initiate Razorpay / Stripe ──
    [HttpPost("subscriptions/initiate")]
    [Authorize]
    public async Task<IActionResult> Initiate([FromBody] InitiateSubscriptionRequest req)
    {
        var userId = GetUserId();
        var result = await _payment.InitiateSubscriptionPaymentAsync(userId, req.PlanId, req.Gateway, req.PromoCode);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new {
            orderId = result.GatewayOrderId,
            amount = result.Amount,
            currency = result.Currency,
            gatewayKey = result.GatewayKey,
            subscriptionId = result.SubscriptionId
        });
    }

    // ── Confirm Razorpay ──
    [HttpPost("subscriptions/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmRazorpay([FromBody] ConfirmPaymentRequest req)
    {
        var userId = GetUserId();
        var result = await _payment.ConfirmSubscriptionPaymentAsync(userId, req);
        if (!result.Success) return BadRequest(new { error = result.Error });
        // Send welcome email
        await _notif.SendSubscriptionConfirmationAsync(userId, result.PlanName!);
        return Ok(new { message = "Subscription activated", subscription = result.Subscription });
    }

    // ── IAP (Google Play / App Store) ──
    [HttpPost("subscriptions/iap/google")]
    [Authorize]
    public async Task<IActionResult> ConfirmGoogleIAP([FromBody] GoogleIAPRequest req)
    {
        var userId = GetUserId();
        var result = await _payment.VerifyGooglePlayPurchaseAsync(userId, req.ProductId, req.PurchaseToken);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { message = "Subscription activated via Google Play" });
    }

    [HttpPost("subscriptions/iap/apple")]
    [Authorize]
    public async Task<IActionResult> ConfirmAppleIAP([FromBody] AppleIAPRequest req)
    {
        var userId = GetUserId();
        var result = await _payment.VerifyApplePurchaseAsync(userId, req.ReceiptData);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { message = "Subscription activated via App Store" });
    }

    [HttpPost("subscriptions/cancel")]
    [Authorize]
    public async Task<IActionResult> Cancel([FromBody] CancelSubscriptionRequest req)
    {
        var userId = GetUserId();
        var result = await _sub.CancelSubscriptionAsync(userId, req.Reason);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { message = "Auto-renewal cancelled. Access continues until period end." });
    }

    // ── PPV ──
    [HttpPost("ppv/{contentId}/purchase")]
    [Authorize]
    public async Task<IActionResult> InitiatePPV(int contentId)
    {
        var userId = GetUserId();
        var result = await _payment.InitiatePPVPaymentAsync(userId, contentId, null);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { orderId = result.GatewayOrderId, amount = result.Amount, currency = result.Currency });
    }

    [HttpPost("ppv/confirm")]
    [Authorize]
    public async Task<IActionResult> ConfirmPPV([FromBody] ConfirmPaymentRequest req)
    {
        var userId = GetUserId();
        var result = await _payment.ConfirmPPVPaymentAsync(userId, req);
        if (!result.Success) return BadRequest(new { error = result.Error });
        return Ok(new { message = "PPV purchase successful", accessUntil = result.AccessExpiresAt });
    }

    // ── Promo Code Validation ──
    [HttpPost("promo/validate")]
    [Authorize]
    public async Task<IActionResult> ValidatePromo([FromBody] ValidatePromoRequest req)
    {
        var result = await _sub.ValidatePromoCodeAsync(req.Code, req.PlanId);
        if (!result.Valid) return BadRequest(new { error = result.Error });
        return Ok(new { discountType = result.DiscountType, discountValue = result.DiscountValue, finalAmount = result.FinalAmount });
    }

    // ── Invoices ──
    [HttpGet("invoices")]
    [Authorize]
    public async Task<IActionResult> GetInvoices([FromQuery] int page = 1)
    {
        var userId = GetUserId();
        return Ok(await _payment.GetInvoicesAsync(userId, page));
    }

    // ── Razorpay Webhook ──
    [HttpPost("webhooks/razorpay")]
    [AllowAnonymous]
    public async Task<IActionResult> RazorpayWebhook()
    {
        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["X-Razorpay-Signature"].ToString();
        var result = await _payment.ProcessRazorpayWebhookAsync(body, signature);
        return result ? Ok() : BadRequest();
    }

    // ── Google Play Webhook ──
    [HttpPost("webhooks/google-play")]
    [AllowAnonymous]
    public async Task<IActionResult> GooglePlayWebhook([FromBody] object payload)
    {
        await _payment.ProcessGooglePlayWebhookAsync(payload.ToString()!);
        return Ok();
    }

    // ── App Store Webhook ──
    [HttpPost("webhooks/app-store")]
    [AllowAnonymous]
    public async Task<IActionResult> AppStoreWebhook()
    {
        var body = await new StreamReader(Request.Body).ReadToEndAsync();
        await _payment.ProcessAppStoreWebhookAsync(body);
        return Ok();
    }

    private int GetUserId() => int.Parse(User.FindFirst("sub")!.Value);
}
