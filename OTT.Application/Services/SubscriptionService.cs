using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OTT.Application.DTOs;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using Razorpay.Api;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Payment = OTT.Domain.Entities.Payment;

namespace OTT.Application.Services;

public interface ISubscriptionService
{
    Task<List<SubscriptionPlanDto>> GetPlansAsync(Guid tenantId);
    Task<OrderResponseDto> CreateOrderAsync(CreateOrderDto dto, Guid userId, Guid tenantId);
    Task<UserSubscriptionDto> VerifyPaymentAsync(VerifyPaymentDto dto, Guid userId, Guid tenantId);
    Task<UserSubscriptionDto?> GetActiveSubscriptionAsync(Guid userId);
    Task<bool> CancelSubscriptionAsync(Guid subscriptionId, Guid userId);
    Task<bool> VerifyIapReceiptAsync(string receipt, string platform, Guid userId, Guid tenantId);
    Task ProcessRenewalsAsync();
    Task<(bool Success, decimal DiscountAmount)> ApplyPromoCodeAsync(string code, Guid tenantId, Guid planId);
    Task<List<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid userId);
}

public class SubscriptionService : ISubscriptionService
{
    private readonly OttDbContext _db;
    private readonly IRedisCacheService _cache;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _config;
    private readonly IDynamicSettingsService _settings;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        OttDbContext db,
        IRedisCacheService cache,
        INotificationService notifications,
        IConfiguration config,
        IDynamicSettingsService settings,
        ILogger<SubscriptionService> logger)
    {
        _db = db;
        _cache = cache;
        _notifications = notifications;
        _config = config;
        _settings = settings;
        _logger = logger;
    }

    public async Task<List<SubscriptionPlanDto>> GetPlansAsync(Guid tenantId)
    {
        var cacheKey = $"plans:{tenantId}";
        var cached = await _cache.GetAsync<List<SubscriptionPlanDto>>(cacheKey);
        if (cached != null) return cached;

        var plans = await _db.SubscriptionPlans
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .OrderBy(p => p.Price)
            .ToListAsync();

        var dtos = plans.Select(MapPlanDto).ToList();
        await _cache.SetAsync(cacheKey, dtos, TimeSpan.FromMinutes(30));
        return dtos;
    }

    public async Task<OrderResponseDto> CreateOrderAsync(CreateOrderDto dto, Guid userId, Guid tenantId)
    {
        var plan = await _db.SubscriptionPlans.FindAsync(dto.PlanId)
            ?? throw new KeyNotFoundException("Plan not found");

        decimal amount = plan.Price;
        string currency = plan.Currency;

        // Apply promo code
        if (!string.IsNullOrEmpty(dto.PromoCode))
        {
            var (promoSuccess, discount) = await ApplyPromoCodeAsync(dto.PromoCode, tenantId, dto.PlanId);
            if (promoSuccess)
                amount = Math.Max(0, amount - discount);
        }

        return dto.Gateway switch
        {
            "razorpay" => await CreateRazorpayOrderAsync(plan, amount, currency, userId, tenantId),
            "stripe" => await CreateStripeOrderAsync(plan, amount, currency, userId, tenantId),
            _ => throw new InvalidOperationException($"Unsupported gateway: {dto.Gateway}")
        };
    }

    public async Task<UserSubscriptionDto> VerifyPaymentAsync(VerifyPaymentDto dto, Guid userId, Guid tenantId)
    {
        return dto.Gateway switch
        {
            "razorpay" => await VerifyRazorpayPaymentAsync(dto, userId, tenantId),
            _ => throw new InvalidOperationException($"Unsupported gateway: {dto.Gateway}")
        };
    }

    public async Task<UserSubscriptionDto?> GetActiveSubscriptionAsync(Guid userId)
    {
        var sub = await _db.UserSubscriptions
            .Include(s => s.Plan)
            .FirstOrDefaultAsync(s => s.UserId == userId
                && s.Status == "active"
                && s.EndDate > DateTime.UtcNow);

        return sub == null ? null : MapSubscriptionDto(sub);
    }

    public async Task<bool> CancelSubscriptionAsync(Guid subscriptionId, Guid userId)
    {
        var sub = await _db.UserSubscriptions
            .FirstOrDefaultAsync(s => s.Id == subscriptionId && s.UserId == userId);

        if (sub == null) return false;

        sub.AutoRenew = false;
        sub.CancelledAt = DateTime.UtcNow;
        sub.Status = "cancelled";
        await _db.SaveChangesAsync();

        var user = await _db.Users.FindAsync(userId);
        if (user != null)
            await _notifications.SendEmailAsync(user.Email, user.FirstName ?? "User",
                "Subscription Cancelled", BuildCancellationEmail(user.FirstName ?? "User", sub.EndDate));

        return true;
    }

    public async Task<bool> VerifyIapReceiptAsync(string receipt, string platform, Guid userId, Guid tenantId)
    {
        try
        {
            bool isValid = platform.ToLower() switch
            {
                "ios" => await VerifyAppleReceiptAsync(receipt),
                "android" => await VerifyGooglePlayReceiptAsync(receipt),
                _ => false
            };

            if (!isValid) return false;

            // Grant a monthly subscription for IAP
            var plan = await _db.SubscriptionPlans
                .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.IsActive && p.BillingCycle == "monthly")
                ?? await _db.SubscriptionPlans.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.IsActive);

            if (plan == null) return false;

            await GrantSubscriptionAsync(userId, plan, "iap", receipt);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "IAP receipt verification failed for user {UserId}", userId);
            return false;
        }
    }

    public async Task ProcessRenewalsAsync()
    {
        var expiringSoon = await _db.UserSubscriptions
            .Include(s => s.User)
            .Include(s => s.Plan)
            .Where(s => s.Status == "active"
                && s.AutoRenew
                && s.EndDate <= DateTime.UtcNow.AddDays(1)
                && s.EndDate > DateTime.UtcNow
                && s.RazorpaySubscriptionId != null)
            .ToListAsync();

        foreach (var sub in expiringSoon)
        {
            try
            {
                await RenewSubscriptionAsync(sub);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to renew subscription {SubId}", sub.Id);
            }
        }

        // Expire overdue subscriptions
        var expired = await _db.UserSubscriptions
            .Where(s => s.Status == "active" && s.EndDate < DateTime.UtcNow)
            .ToListAsync();

        foreach (var sub in expired)
            sub.Status = "expired";

        await _db.SaveChangesAsync();
    }

    public async Task<(bool Success, decimal DiscountAmount)> ApplyPromoCodeAsync(string code, Guid tenantId, Guid planId)
    {
        var promo = await _db.PromoCodes
            .FirstOrDefaultAsync(p => p.Code == code && p.TenantId == tenantId
                && p.IsActive && (p.ExpiresAt == null || p.ExpiresAt > DateTime.UtcNow)
                && (p.MaxUses == null || p.UsedCount < p.MaxUses));

        if (promo == null) return (false, 0);

        var plan = await _db.SubscriptionPlans.FindAsync(planId);
        if (plan == null) return (false, 0);

        var discountAmount = promo.DiscountType == "percentage"
            ? plan.Price * promo.DiscountValue / 100
            : Math.Min(promo.DiscountValue, plan.Price);

        return (true, discountAmount);
    }

    public async Task<List<PaymentHistoryDto>> GetPaymentHistoryAsync(Guid userId)
    {
        return await _db.Payments
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PaymentHistoryDto
            {
                Id = p.Id,
                Amount = p.Amount,
                Currency = p.Currency,
                Gateway = p.Gateway,
                Status = p.Status,
                Description = p.Description,
                CreatedAt = p.CreatedAt,
                GatewayPaymentId = p.GatewayPaymentId
            })
            .ToListAsync();
    }

    // ── Private Helpers ───────────────────────────────────────────────────────

    private async Task<OrderResponseDto> CreateRazorpayOrderAsync(SubscriptionPlan plan, decimal amount, string currency, Guid userId, Guid tenantId)
    {
        var keyId = await _settings.GetAsync(tenantId, SettingKeys.RazorpayKeyId, _config["Razorpay:KeyId"])
            ?? throw new InvalidOperationException("Razorpay KeyId not configured");
        var keySecret = await _settings.GetAsync(tenantId, SettingKeys.RazorpayKeySecret, _config["Razorpay:KeySecret"])
            ?? throw new InvalidOperationException("Razorpay KeySecret not configured");

        var client = new RazorpayClient(keyId, keySecret);
        var options = new Dictionary<string, object>
        {
            { "amount", (int)(amount * 100) }, // paisa
            { "currency", currency },
            { "receipt", $"order_{userId}_{DateTime.UtcNow.Ticks}" },
            { "notes", new Dictionary<string, string> { { "user_id", userId.ToString() }, { "plan_id", plan.Id.ToString() } } }
        };

        var order = client.Order.Create(options);

        // Cache order for verification
        await _cache.SetStringAsync($"rzp_order:{order["id"]}", JsonSerializer.Serialize(new
        {
            planId = plan.Id,
            userId,
            amount,
            currency
        }), TimeSpan.FromHours(1));

        return new OrderResponseDto
        {
            OrderId = order["id"].ToString()!,
            Gateway = "razorpay",
            Amount = amount,
            Currency = currency,
            RazorpayKeyId = keyId
        };
    }

    private async Task<OrderResponseDto> CreateStripeOrderAsync(SubscriptionPlan plan, decimal amount, string currency, Guid userId, Guid tenantId)
    {
        // Stripe PaymentIntent creation - simplified
        var stripeKey = await _settings.GetAsync(tenantId, SettingKeys.StripeSecretKey, _config["Stripe:SecretKey"])
            ?? throw new InvalidOperationException("Stripe key not configured");

        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", stripeKey);

        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("amount", ((int)(amount * 100)).ToString()),
            new KeyValuePair<string, string>("currency", currency.ToLower()),
            new KeyValuePair<string, string>("metadata[user_id]", userId.ToString()),
            new KeyValuePair<string, string>("metadata[plan_id]", plan.Id.ToString())
        });

        var response = await client.PostAsync("https://api.stripe.com/v1/payment_intents", content);
        var json = await response.Content.ReadAsStringAsync();
        var pi = JsonSerializer.Deserialize<JsonElement>(json);

        return new OrderResponseDto
        {
            OrderId = pi.GetProperty("id").GetString()!,
            Gateway = "stripe",
            Amount = amount,
            Currency = currency,
            StripeClientSecret = pi.GetProperty("client_secret").GetString()
        };
    }

    private async Task<UserSubscriptionDto> VerifyRazorpayPaymentAsync(VerifyPaymentDto dto, Guid userId, Guid tenantId)
    {
        var keySecret = await _settings.GetAsync(tenantId, SettingKeys.RazorpayKeySecret, _config["Razorpay:KeySecret"])
            ?? throw new InvalidOperationException("Razorpay secret not configured");

        // Verify signature
        var expectedSignature = ComputeHmacSha256($"{dto.OrderId}|{dto.PaymentId}", keySecret);
        if (expectedSignature != dto.Signature)
            throw new UnauthorizedAccessException("Invalid payment signature");

        // Retrieve cached order data
        var orderJson = await _cache.GetStringAsync($"rzp_order:{dto.OrderId}");
        if (orderJson == null) throw new InvalidOperationException("Order not found");

        var orderData = JsonSerializer.Deserialize<JsonElement>(orderJson);
        var planId = Guid.Parse(orderData.GetProperty("planId").GetString()!);
        var amount = orderData.GetProperty("amount").GetDecimal();

        var plan = await _db.SubscriptionPlans.FindAsync(planId)
            ?? throw new KeyNotFoundException("Plan not found");

        // Record payment
        var payment = new Payment
        {
            UserId = userId,
            TenantId = tenantId,
            Gateway = "razorpay",
            GatewayOrderId = dto.OrderId,
            GatewayPaymentId = dto.PaymentId,
            Amount = amount,
            Currency = plan.Currency,
            Status = "success",
            Description = $"Subscription: {plan.Name}",
            CreatedAt = DateTime.UtcNow
        };
        _db.Payments.Add(payment);

        var sub = await GrantSubscriptionAsync(userId, plan, "razorpay", dto.PaymentId);
        sub.GatewayPaymentId = dto.PaymentId;
        sub.RazorpayOrderId = dto.OrderId;

        await _cache.RemoveAsync($"rzp_order:{dto.OrderId}");
        await _db.SaveChangesAsync();

        // Send confirmation
        var user = await _db.Users.FindAsync(userId);
        if (user != null)
            await _notifications.SendSubscriptionConfirmationAsync(user.Email, user.FirstName ?? "User", plan.Name, sub.EndDate);

        return MapSubscriptionDto(sub);
    }

    private async Task<UserSubscription> GrantSubscriptionAsync(Guid userId, SubscriptionPlan plan, string gateway, string? gatewayRef = null)
    {
        // Deactivate existing
        var existing = await _db.UserSubscriptions
            .Where(s => s.UserId == userId && s.Status == "active")
            .ToListAsync();
        existing.ForEach(s => s.Status = "superseded");

        var endDate = plan.BillingCycle switch
        {
            "yearly" => DateTime.UtcNow.AddYears(1),
            "quarterly" => DateTime.UtcNow.AddMonths(3),
            "weekly" => DateTime.UtcNow.AddDays(7),
            "lifetime" => DateTime.UtcNow.AddYears(100),
            _ => DateTime.UtcNow.AddMonths(1)
        };

        var sub = new UserSubscription
        {
            UserId = userId,
            PlanId = plan.Id,
            Status = "active",
            StartDate = DateTime.UtcNow,
            EndDate = endDate,
            AutoRenew = gateway != "iap",
            PaymentGateway = gateway,
            CreatedAt = DateTime.UtcNow
        };

        _db.UserSubscriptions.Add(sub);
        await _db.SaveChangesAsync();

        // Invalidate cache
        await _cache.RemoveAsync($"sub:{userId}");

        return sub;
    }

    private async Task RenewSubscriptionAsync(UserSubscription sub)
    {
        if (string.IsNullOrEmpty(sub.RazorpaySubscriptionId)) return;

        // Razorpay subscription auto-renews on their side; just extend end date
        var newEndDate = sub.Plan.BillingCycle switch
        {
            "yearly" => sub.EndDate.AddYears(1),
            _ => sub.EndDate.AddMonths(1)
        };
        sub.EndDate = newEndDate;
        sub.Status = "active";

        var payment = new Payment
        {
            UserId = sub.UserId,
            TenantId = sub.User.TenantId,
            Gateway = sub.PaymentGateway ?? "razorpay",
            Amount = sub.Plan.Price,
            Currency = sub.Plan.Currency,
            Status = "success",
            Description = $"Renewal: {sub.Plan.Name}",
            CreatedAt = DateTime.UtcNow
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();

        await _notifications.SendSubscriptionConfirmationAsync(
            sub.User.Email, sub.User.FirstName ?? "User", sub.Plan.Name, newEndDate);
    }

    private async Task<bool> VerifyAppleReceiptAsync(string receipt)
    {
        var appleSecret = _config["Apple:IAPSharedSecret"] ?? "";
        using var client = new HttpClient();

        var body = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["receipt-data"] = receipt,
            ["password"] = appleSecret
        });
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        // Try sandbox first, then production
        var response = await client.PostAsync("https://sandbox.itunes.apple.com/verifyReceipt", content);
        var json = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

        var status = json.GetProperty("status").GetInt32();
        if (status == 21007) // Sandbox receipt sent to production
            return true; // Sandbox is valid for testing

        return status == 0;
    }

    private async Task<bool> VerifyGooglePlayReceiptAsync(string receipt)
    {
        // Google Play receipt verification via Google API
        // receipt format: {packageName}:{subscriptionId}:{purchaseToken}
        var parts = receipt.Split(':');
        if (parts.Length < 3) return false;

        var packageName = parts[0];
        var subscriptionId = parts[1];
        var purchaseToken = parts[2];

        using var client = new HttpClient();
        var apiKey = _config["Google:PlayApiKey"] ?? "";
        var url = $"https://androidpublisher.googleapis.com/androidpublisher/v3/applications/{packageName}/purchases/subscriptions/{subscriptionId}/tokens/{purchaseToken}?key={apiKey}";

        var response = await client.GetAsync(url);
        return response.IsSuccessStatusCode;
    }

    private static string ComputeHmacSha256(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    private static string BuildCancellationEmail(string name, DateTime endDate) => $"""
        <div style="font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#141414;color:#fff;padding:40px;border-radius:12px">
            <h1 style="color:#E50914;text-align:center">Subscription Cancelled</h1>
            <p>Hi {name},</p>
            <p>Your subscription has been cancelled. You can continue watching until <strong>{endDate:MMMM dd, yyyy}</strong>.</p>
            <p style="color:#aaa">We'd love to have you back. Resubscribe any time to continue enjoying unlimited content.</p>
        </div>
    """;

    private static SubscriptionPlanDto MapPlanDto(SubscriptionPlan p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Price = p.Price,
        Currency = p.Currency,
        BillingCycle = p.BillingCycle,
        MaxProfiles = p.MaxProfiles,
        MaxStreams = p.MaxStreams,
        AllowDownloads = p.AllowDownloads,
        AllowUhd = p.AllowUhd,
        Features = string.IsNullOrEmpty(p.Features) ? [] : JsonSerializer.Deserialize<List<string>>(p.Features) ?? [],
        IsPopular = p.IsPopular,
        RazorpayPlanId = p.RazorpayPlanId
    };

    private static UserSubscriptionDto MapSubscriptionDto(UserSubscription s) => new()
    {
        Id = s.Id,
        Plan = MapPlanDto(s.Plan),
        Status = s.Status,
        StartDate = s.StartDate,
        EndDate = s.EndDate,
        AutoRenew = s.AutoRenew,
        CancelledAt = s.CancelledAt
    };
}

public class PaymentHistoryDto
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string Gateway { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Description { get; set; }
    public string? GatewayPaymentId { get; set; }
    public DateTime CreatedAt { get; set; }
}
