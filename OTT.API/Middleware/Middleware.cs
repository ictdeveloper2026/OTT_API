using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;

namespace OTT.API.Middleware;

// ── Global Exception Handler ───────────────────────────────────────────────────

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message) = exception switch
        {
            KeyNotFoundException => (HttpStatusCode.NotFound, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, exception.Message),
            InvalidOperationException => (HttpStatusCode.BadRequest, exception.Message),
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred")
        };

        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            success = false,
            message,
            errors = new[] { message },
            traceId = context.TraceIdentifier
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

// ── Multi-Tenant Resolver ─────────────────────────────────────────────────────

public class TenantMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, OttDbContext db, IRedisCacheService cache)
    {
        Guid? tenantId = null;

        // 1. From X-Tenant-ID header
        if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var tenantHeader)
            && Guid.TryParse(tenantHeader.FirstOrDefault(), out var headerTenantId))
        {
            tenantId = headerTenantId;
        }
        // 2. From subdomain: {slug}.yourdomain.com
        else
        {
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length >= 3)
            {
                var slug = parts[0].ToLower();
                if (slug != "www" && slug != "api" && slug != "admin")
                {
                    var cacheKey = $"tenant_slug:{slug}";
                    var cached = await cache.GetStringAsync(cacheKey);

                    if (cached != null && Guid.TryParse(cached, out var cachedId))
                    {
                        tenantId = cachedId;
                    }
                    else
                    {
                        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == slug && t.IsActive);
                        if (tenant != null)
                        {
                            await cache.SetStringAsync(cacheKey, tenant.Id.ToString(), TimeSpan.FromMinutes(30));
                            tenantId = tenant.Id;
                        }
                    }
                }
            }
        }

        // 3. Default tenant (for development / single-tenant mode)
        if (!tenantId.HasValue)
        {
            var defaultSlug = context.RequestServices.GetRequiredService<IConfiguration>()["App:DefaultTenantSlug"] ?? "default";
            var cacheKey = $"tenant_slug:{defaultSlug}";
            var cached = await cache.GetStringAsync(cacheKey);

            if (cached != null && Guid.TryParse(cached, out var cachedId))
            {
                tenantId = cachedId;
            }
            else
            {
                var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Slug == defaultSlug);
                if (tenant != null)
                {
                    await cache.SetStringAsync(cacheKey, tenant.Id.ToString(), TimeSpan.FromHours(1));
                    tenantId = tenant.Id;
                }
            }
        }

        if (tenantId.HasValue)
            context.Items["TenantId"] = tenantId.Value;

        await _next(context);
    }
}

// ── Request Logging Middleware ────────────────────────────────────────────────

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var start = DateTime.UtcNow;
        await _next(context);
        var elapsed = (DateTime.UtcNow - start).TotalMilliseconds;

        _logger.LogInformation("{Method} {Path} {Status} {Elapsed}ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            elapsed);
    }
}

// ── HTTP Context Extension ────────────────────────────────────────────────────

public static class HttpContextExtensions
{
    public static Guid GetTenantId(this HttpContext context)
    {
        if (context.Items.TryGetValue("TenantId", out var tenantId) && tenantId is Guid id)
            return id;
        throw new InvalidOperationException("Tenant not resolved");
    }

    public static Guid GetTenantIdOrDefault(this HttpContext context, Guid defaultId = default)
    {
        if (context.Items.TryGetValue("TenantId", out var tenantId) && tenantId is Guid id)
            return id;
        return defaultId;
    }

    public static Guid? GetUserId(this HttpContext context)
    {
        var claim = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (claim != null && Guid.TryParse(claim.Value, out var userId))
            return userId;
        return null;
    }

    public static Guid RequireUserId(this HttpContext context)
    {
        var id = context.GetUserId();
        if (!id.HasValue)
            throw new UnauthorizedAccessException("Authentication required");
        return id.Value;
    }
}
