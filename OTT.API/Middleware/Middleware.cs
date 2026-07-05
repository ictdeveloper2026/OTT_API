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

        // 0. Authenticated requests: the tenant baked into the JWT is authoritative.
        //    This prevents X-Tenant-ID spoofing (a logged-in user operating inside
        //    another tenant by sending an arbitrary header).
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirst("tenant_id")?.Value, out var claimTenant))
        {
            if (context.Request.Headers.TryGetValue("X-Tenant-ID", out var hdr)
                && Guid.TryParse(hdr.FirstOrDefault(), out var hdrTenant)
                && hdrTenant != claimTenant)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{\"success\":false,\"message\":\"Tenant mismatch\"}");
                return;
            }

            context.Items["TenantId"] = claimTenant;
            await _next(context);
            return;
        }

        // 1. From X-Tenant-ID header (anonymous bootstrap: login/register/public config)
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

    public static Guid? GetProfileId(this HttpContext context)
    {
        var claim = context.User.FindFirst("profile_id");
        if (claim != null && Guid.TryParse(claim.Value, out var profileId))
            return profileId;
        return null;
    }

    public static Guid RequireProfileId(this HttpContext context)
    {
        var id = context.GetProfileId();
        if (!id.HasValue)
            throw new UnauthorizedAccessException("No active profile selected");
        return id.Value;
    }

    /// <summary>
    /// Resolves the per-request viewer context (tenant + platform/device/country) that producers
    /// stamp onto buffered analytics events. Platform/device come from client headers the Flutter
    /// app already sends (X-Platform); country comes from the CDN/edge (CF-IPCountry) when present.
    /// All fields except tenant are best-effort — a missing header simply yields null.
    /// </summary>
    public static OTT.Application.Services.ClientContext GetClientContext(this HttpContext context)
    {
        static string? Header(HttpContext c, string name) =>
            c.Request.Headers.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
                ? v.ToString().Trim()
                : null;

        return new OTT.Application.Services.ClientContext(
            context.GetTenantIdOrDefault(),
            Header(context, "X-Platform"),
            Header(context, "X-Device-Type"),
            Header(context, "CF-IPCountry") ?? Header(context, "X-Country"));
    }
}

// ── Audit Log ──────────────────────────────────────────────────────────────────
// Records every privileged mutating request (POST/PUT/PATCH/DELETE under /api/admin)
// with the acting user, tenant, path and resulting status. Failures here never break
// the request — an audit write must not take down an admin action.
public class AuditMiddleware
{
    private static readonly HashSet<string> Mutating =
        new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "PATCH", "DELETE" };

    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, OttDbContext db, ILogger<AuditMiddleware> logger)
    {
        await _next(context);

        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase)) return;
        if (!Mutating.Contains(context.Request.Method)) return;

        try
        {
            Guid? actorId = Guid.TryParse(
                context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var aid)
                ? aid : null;
            var tenantId = context.GetTenantIdOrDefault();

            db.AuditLogs.Add(new OTT.Domain.Entities.AuditLog
            {
                TenantId = tenantId,
                ActorUserId = actorId,
                ActorEmail = context.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value,
                Action = context.Request.Method,
                Path = path.Length > 512 ? path[..512] : path,
                StatusCode = context.Response.StatusCode,
                IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                UserAgent = context.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
                    ? (ua.Length > 512 ? ua[..512] : ua) : null
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Audit log write failed for {Method} {Path}", context.Request.Method, path);
        }
    }
}

// ── ETag / Conditional GET ─────────────────────────────────────────────────────
// Adds a content-hash ETag to successful GET JSON responses and answers
// If-None-Match with 304 Not Modified, saving bandwidth on unchanged catalog data.
// Sits after UseStaticFiles so large file downloads are already served and not buffered.
public class ETagMiddleware
{
    private readonly RequestDelegate _next;

    public ETagMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Only conditional-GET makes sense; everything else passes straight through.
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);

            var canHash = context.Response.StatusCode == StatusCodes.Status200OK
                && buffer.Length > 0
                && (context.Response.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false);

            if (canHash)
            {
                var hash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)));
                var etag = $"\"{hash[..32]}\""; // 128-bit prefix is plenty for cache validation
                context.Response.Headers.ETag = etag;

                if (context.Request.Headers.IfNoneMatch == etag)
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.ContentLength = null;
                    context.Response.Body = originalBody;
                    return; // body intentionally not written
                }
            }

            buffer.Position = 0;
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }
}
