# ASP.NET Core API — Targeted Review

> Scope: `dotnet_api/` (OTT.API / OTT.Application / OTT.Infrastructure / OTT.Domain). Companion to `../ARCHITECTURE_REVIEW.md` and `../SCALABILITY_ROADMAP.md`; this one scores the specific checklist below.
> **Date:** 2026-06-23 · Legend: ✅ Good · ⚠️ Partial · ❌ Missing/Broken

## Scorecard

| # | Area | Rating |
|---|------|--------|
| 1 | SOLID Principles | ⚠️ Partial |
| 2 | Clean Architecture | ⚠️ Partial |
| 3 | CQRS | ❌ Missing |
| 4 | Repository Pattern | ❌ Missing (DbContext used directly) |
| 5 | Unit of Work | ⚠️ Implicit only |
| 6 | Dependency Injection | ✅ Good |
| 7 | JWT Security | ⚠️ Several issues |
| 8 | Authorization | ❌ Weak (RBAC only, IDOR) |
| 9 | Validation | ❌ Missing entirely |
| 10 | Middleware | ✅ Decent |
| 11 | Exception Handling | ⚠️ Partial |
| 12 | Logging | ⚠️ Partial |
| 13 | Serilog | ⚠️ Basic |
| 14 | EF Core Performance | ❌ Several issues |
| 15 | Database Queries | ❌ Missing indexes / scans |
| 16 | API Versioning | ❌ Missing |
| 17 | Swagger | ✅ Good |
| 18 | Health Checks | ⚠️ Basic |

---

## 1. SOLID — ⚠️ Partial
- **SRP violated:** `ContentService` (790 lines) mixes read queries, admin CRUD, stream-URL building, mapping, and caching. Controllers (`AdminController`, `ProfilesController`, `NotificationsController`, `WatchPartyController`) hold business logic *and* query `OttDbContext` directly.
- **OCP violated:** payment provider `switch` in `SubscriptionService` (`CreateOrderAsync`/`VerifyPaymentAsync`) — adding a gateway means editing existing code.
- **DIP violated:** `OTT.Application` depends on **concrete** Infrastructure types (`OttDbContext`, `IRedisCacheService`, `IJwtTokenService` all live in Infrastructure). The abstractions should live in Application/Domain.
- LSP/ISP largely fine.
**Fix:** split `ContentService` by responsibility; `IPaymentGateway` strategy; move interfaces into Application, implementations into Infrastructure.

## 2. Clean Architecture — ⚠️ Partial
Layers exist (`Domain` → `Application` → `Infrastructure` → `API`) and `Domain` is clean (pure entities). But the **dependency rule leaks both ways**: API references Infrastructure directly (controllers inject `OttDbContext`), and Application references Infrastructure concretes. Domain is **anemic** — all logic sits in services, none on entities.
**Fix:** API depends only on Application; Application defines `IRepository`/`ICacheService`/`IJwtTokenService` interfaces; Infrastructure implements them; register the wiring in `Program.cs`.

## 3. CQRS — ❌ Missing
No MediatR, no command/query types, no separation — fat services serve both reads and writes. For a codebase this size CQRS would meaningfully reduce the 790-line services and give a home for cross-cutting **pipeline behaviors** (validation, auth, caching, logging).
**Fix:**
```csharp
builder.Services.AddMediatR(c => c.RegisterServicesFromAssembly(typeof(GetHomePageQuery).Assembly));
builder.Services.AddValidatorsFromAssembly(...);
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
// Controllers become: return Ok(await _mediator.Send(new GetHomePageQuery(tenantId, profileId)));
```

## 4. Repository Pattern — ❌ Missing
Services use `OttDbContext` directly (`_db.Contents.Where(...)`). This is a *defensible* pragmatic choice, but it couples Application to EF and blocks unit testing without a real database.
**Fix (if adopting):** `IContentRepository`/`IUserRepository` returning domain types; keep EF in the implementation. Or keep DbContext but inject it behind an Application-owned interface so it can be faked.

## 5. Unit of Work — ⚠️ Implicit only
`DbContext` *is* a Unit of Work, but it's used ad-hoc: many methods call `SaveChangesAsync` multiple times per request with **no transaction boundary**. The Razorpay flow (`VerifyRazorpayPaymentAsync`) adds a Payment, grants a subscription (its own `SaveChanges`), then saves again — **not atomic**; a failure mid-way leaves a subscription without a payment row or vice-versa.
**Fix:** wrap multi-write use-cases in an explicit transaction (`await using var tx = await _db.Database.BeginTransactionAsync();`) or a single `SaveChanges` at the end of the use-case.

## 6. Dependency Injection — ✅ Good
Constructor injection throughout; lifetimes are correct. Notably, the **singletons** `DynamicSettingsService`/`DynamicStorageService` correctly use `IServiceScopeFactory` to access the scoped `DbContext` (no captive dependency) — this is done right. SignalR hubs inject `DbContext` (fine — hubs are transient per call); `HubService` singleton only takes `IHubContext` (safe). Async all the way — no `.Result`/`.Wait()` blocking.
**Minor:** `IHttpClientFactory` is registered (`AddHttpClient()`) but **never used** — 9 `new HttpClient()` instances remain in `AuthService`/`SubscriptionService` (socket-exhaustion risk). Some controllers depend on `OttDbContext` (see §2).

## 7. JWT Security — ⚠️ Several issues
- **HS256 symmetric** key from config; weak default secret ships (`appsettings.json:17`, docker-compose fallback). No fail-fast, no rotation.
- `JwtTokenService.ValidateToken` sets `ValidateLifetime = false` (`:89`) — intentional for the refresh path, but the method is general-purpose; reusing it elsewhere would accept expired tokens.
- **Refresh tokens stored in plaintext** in the DB (`AuthService.GenerateAuthResponse`); no per-device binding.
- `ClockSkew` is sensibly tightened (1 min in `Program.cs`, 0 in the service).
**Fix:** fail-fast on weak/placeholder secret; store SHA-256 hashes of refresh tokens; consider asymmetric (RS256) if other services must validate tokens; never reuse a `ValidateLifetime=false` validator for authn.

## 8. Authorization — ❌ Weak
- **RBAC only**, via magic strings (`[Authorize(Roles = "admin")]`, `"admin,creator"`). No policies, no resource-based authorization.
- **No object-level ownership checks** → cross-tenant IDOR: `AdminController` edits/deletes banners/plans/users by `FindAsync(id)` with no `TenantId` filter; admin of tenant A can mutate tenant B's data.
- **No streaming entitlement** — any authenticated user gets signed URLs for any paid title (`ContentsController.cs:81`).
- Tenant is trusted from the `X-Tenant-ID` header without matching the JWT claim.
**Fix:** policy-based auth (`AddAuthorizationBuilder().AddPolicy("CanManageContent", …)`); resource-based handlers (`IAuthorizationService.AuthorizeAsync(user, content, "Owner")`); global tenant query filter; entitlement gate before issuing stream URLs.

## 9. Validation — ❌ Missing entirely
No DataAnnotations, no FluentValidation, no `ModelState` checks (confirmed: zero `[Required]`/`[EmailAddress]`/`MaxLength` in DTOs). Invalid input flows until it throws deep in a service. E.g. `RegisterRequestDto.Password` has no length/complexity rule; `RateRequestDto.Rating` isn't bounded.
**Fix:** FluentValidation + auto-400:
```csharp
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequestDto> {
    public RegisterRequestValidator() {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).MinimumLength(8).Matches("[A-Z]").Matches("[0-9]");
    }
}
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
```

## 10. Middleware — ✅ Decent
Three custom middlewares (`ExceptionMiddleware`, `RequestLoggingMiddleware`, `TenantMiddleware`) and sane ordering (exception → logging → CORS → static → auth → authz → tenant → endpoints). Two notes: `RequestLoggingMiddleware` reinvents what `UseSerilogRequestLogging()` does better; `TenantMiddleware` runs after auth (good) but trusts the header (see §8).
**Fix:** replace the custom request logger with `app.UseSerilogRequestLogging()`; add a security-headers middleware (HSTS/CSP/X-Content-Type-Options).

## 11. Exception Handling — ⚠️ Partial
Global `ExceptionMiddleware` maps exception types → status codes — good centralization. But: it **uses exceptions for normal control flow** (business rules throw `InvalidOperationException`/`UnauthorizedAccessException`), it **returns `exception.Message` to the client** for several types (info leak risk if an internal exception reuses those types), and responses aren't **RFC 7807 ProblemDetails**.
**Fix:** adopt `IExceptionHandler` (.NET 8) + `ProblemDetails`; introduce a typed `DomainException`/`Result<T>` for expected business failures instead of exceptions; only map known domain exceptions to messages, everything else → generic 500 + traceId.

## 12. Logging — ⚠️ Partial
Serilog writes console + rolling file. Gaps: **file sink is useless in containers** (ephemeral); no correlation/trace IDs beyond `TraceIdentifier`; no structured request logging with status/elapsed/user; PII/secret scrubbing not enforced (OTPs, tokens, receipts pass through services). Log levels per environment are set (good).
**Fix:** add a structured sink (Seq/Elasticsearch/OTLP); `UseSerilogRequestLogging`; enrich with correlation id + tenant + user; destructuring policies to redact secrets.

## 13. Serilog — ⚠️ Basic
Configured via `ReadFrom.Configuration` + console/file + `FromLogContext`. Functional but minimal.
**Fix:** `Enrich.WithCorrelationId()`, `Enrich.WithSpan()` (OpenTelemetry), environment/version enrichers, and remove the file sink in favor of a centralized sink for production.

## 14. EF Core Performance — ❌ Several issues
- **No `AsNoTracking()`** on read queries — everything is change-tracked, adding overhead and memory on large lists.
- **Cartesian explosion:** `GetContentDetailAsync` `Include`s `Seasons.Episodes` + `ContentGenres.Genre` + `ContentCasts` + `ContentTags.Tag` in **one** query → row multiplication. Use `AsSplitQuery()`.
- **Write-on-read:** `ViewCount++` + `SaveChanges` on every detail view (`ContentService.cs:195`).
- **Client-side projection:** `.Select(c => MapContentListItem(c))` can't translate, so full entities are materialized then mapped in memory.
- **Load-all-then-aggregate:** `RateContentAsync` pulls every rating to compute an average (`:444`).
- **MARS enabled** (`MultipleActiveResultSets=True`) — contention footgun.
- No compiled queries for hot paths.
**Fix:** `AsNoTracking()` + `AsSplitQuery()` on read paths; project to DTOs in SQL; aggregate with `AVG()`; move view counts to Redis; drop MARS.

## 15. Database Queries — ❌ Missing indexes / scans
- **No indexes** on the most-filtered columns: `Content(TenantId, Status)`, `Payment(TenantId, Status, CreatedAt)`, `UserSubscription(UserId, Status, EndDate)`. Core list/dashboard queries scan.
- **OFFSET pagination** (`Skip/Take`) degrades on deep pages → use keyset/cursor.
- **In-memory grouping** for revenue (`AdminController.GetRevenue`) — acceptable now, but unindexed `Payment` makes the underlying fetch a scan.
- **`Contains(list)`** on potentially large id lists in `GetHomePageAsync`.
**Fix:** add the composite indexes; keyset pagination; ensure `LIKE '%q%'` search moves to a full-text engine (Elasticsearch) at scale.

## 16. API Versioning — ❌ Missing
No `Asp.Versioning`; routes are `/api/...` (the Flutter client even comments on the ambiguity). A breaking change will break deployed mobile clients you can't force-update.
**Fix:**
```csharp
builder.Services.AddApiVersioning(o => {
    o.DefaultApiVersion = new ApiVersion(1, 0);
    o.AssumeDefaultVersionWhenUnspecified = true;
    o.ReportApiVersions = true;
}).AddApiExplorer(o => { o.GroupNameFormat = "'v'VVV"; o.SubstituteApiVersionInUrl = true; });
// [ApiVersion("1.0")] [Route("api/v{version:apiVersion}/contents")]
```

## 17. Swagger — ✅ Good
`AddSwaggerGen` with a JWT Bearer security definition + requirement, served in Development only — all correct.
**Improvements:** include XML doc comments (`opts.IncludeXmlComments(...)`), per-version documents (after §16), request/response examples, a `ProblemDetails` schema, and consider exposing it in production behind authentication for partner integration.

## 18. Health Checks — ⚠️ Basic
`MapHealthChecks("/health")` with SQL Server + Redis registered — a good start, but a **single endpoint** with no liveness/readiness split, no tags, no JSON detail, and no startup probe. Orchestrators (k8s/ECS) need `live` (process up) vs `ready` (dependencies reachable) to avoid routing traffic before dependencies are warm.
**Fix:**
```csharp
app.MapHealthChecks("/health/live",  new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() {
    Predicate = c => c.Tags.Contains("ready"),
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
// tag the SQL/Redis checks with "ready"; keep S3 as a non-fatal "degraded" check
```

---

## Improvement Recommendations — Prioritized

**P0 — correctness & security**
1. Validation layer (FluentValidation + auto-400).
2. Authorization hardening: policies + resource ownership + global tenant filter + entitlement gate.
3. JWT: fail-fast on weak secret; hash refresh tokens.
4. Atomic payment use-case (single transaction).

**P1 — performance & correctness under load**
5. `AsNoTracking` + `AsSplitQuery` + SQL projections; drop MARS.
6. Composite indexes; keyset pagination.
7. `IHttpClientFactory` instead of `new HttpClient()`.
8. Move `ViewCount`/progress writes off the synchronous path (Redis).

**P2 — architecture & operability**
9. CQRS/MediatR + pipeline behaviors; split `ContentService`; `IPaymentGateway` strategy.
10. Move interfaces into Application (fix DIP); repositories optional.
11. `IExceptionHandler` + `ProblemDetails`; typed domain failures.
12. `UseSerilogRequestLogging` + structured sink + correlation IDs.
13. API versioning + versioned Swagger.
14. Split health checks (live/ready) + startup probe.

**Already good — keep:** DI lifetimes & scope handling, async correctness, Swagger Bearer setup, centralized exception middleware, middleware ordering, Serilog wiring baseline.
