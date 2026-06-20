using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OTT.Application.DTOs;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using BC = BCrypt.Net.BCrypt;

namespace OTT.Application.Services;

public interface IAuthService
{
    Task<AuthResponseDto> LoginAsync(LoginRequestDto request, Guid tenantId);
    Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, Guid tenantId);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken);
    Task<bool> SendOtpAsync(string email, Guid tenantId);
    Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto request, Guid tenantId);
    Task<AuthResponseDto> SocialLoginAsync(SocialLoginDto request, Guid tenantId);
    Task<bool> LogoutAsync(string refreshToken);
    Task<bool> ForgotPasswordAsync(string email, Guid tenantId);
    Task<bool> ResetPasswordAsync(ResetPasswordDto request);
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto request);
    Task<bool> VerifyEmailAsync(string token);
    Task<AuthResponseDto> SelectProfileAsync(Guid userId, Guid profileId);
}

public class AuthService : IAuthService
{
    private readonly OttDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IRedisCacheService _cache;
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        OttDbContext db,
        IJwtTokenService jwt,
        IRedisCacheService cache,
        INotificationService notificationService,
        IConfiguration config,
        ILogger<AuthService> logger)
    {
        _db = db;
        _jwt = jwt;
        _cache = cache;
        _notificationService = notificationService;
        _config = config;
        _logger = logger;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequestDto request, Guid tenantId)
    {
        var user = await _db.Users
            .Include(u => u.Profiles)
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower() && u.TenantId == tenantId && !u.IsDeleted);

        if (user == null)
            throw new UnauthorizedAccessException("Invalid credentials");

        if (!user.IsEmailVerified && bool.TryParse(_config["App:RequireEmailVerification"], out var requireVerification) && requireVerification)
            throw new InvalidOperationException("Please verify your email before logging in");

        if (user.IsBlocked)
            throw new UnauthorizedAccessException("Your account has been suspended");

        if (!BC.Verify(request.Password, user.PasswordHash))
            throw new UnauthorizedAccessException("Invalid credentials");

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GenerateAuthResponse(user);
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequestDto request, Guid tenantId)
    {
        var existing = await _db.Users.AnyAsync(u => u.Email == request.Email.ToLower() && u.TenantId == tenantId);
        if (existing)
            throw new InvalidOperationException("Email already registered");

        var user = new User
        {
            TenantId = tenantId,
            Email = request.Email.ToLower(),
            PasswordHash = BC.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Phone = request.Phone,
            Role = "viewer",
            AuthProvider = "local",
            IsEmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Users.Add(user);

        // Create default profile
        var defaultProfile = new UserProfile
        {
            UserId = user.Id,
            Name = request.FirstName,
            IsDefault = true,
            MaturityLevel = "all",
            AvatarUrl = $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(request.FirstName)}&background=E50914&color=fff"
        };
        _db.UserProfiles.Add(defaultProfile);

        await _db.SaveChangesAsync();

        // Send verification email
        var verifyToken = Guid.NewGuid().ToString("N");
        await _cache.SetStringAsync($"email_verify:{verifyToken}", user.Id.ToString(), TimeSpan.FromHours(24));
        await _notificationService.SendEmailVerificationAsync(user.Email, user.FirstName, verifyToken);

        return await GenerateAuthResponse(user);
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken)
    {
        var storedToken = await _db.RefreshTokens
            .Include(r => r.User).ThenInclude(u => u.Profiles)
            .FirstOrDefaultAsync(r => r.Token == refreshToken && !r.IsRevoked);

        if (storedToken == null || storedToken.ExpiresAt < DateTime.UtcNow)
            throw new UnauthorizedAccessException("Invalid or expired refresh token");

        // Rotate refresh token
        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;

        var response = await GenerateAuthResponse(storedToken.User);
        await _db.SaveChangesAsync();
        return response;
    }

    public async Task<bool> SendOtpAsync(string email, Guid tenantId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower() && u.TenantId == tenantId);

        var otp = new Random().Next(100000, 999999).ToString();
        await _cache.SetStringAsync($"otp:{email}:{tenantId}", otp, TimeSpan.FromMinutes(10));

        var name = user?.FirstName ?? "User";
        await _notificationService.SendOtpEmailAsync(email, name, otp);
        return true;
    }

    public async Task<AuthResponseDto> VerifyOtpAsync(VerifyOtpDto request, Guid tenantId)
    {
        var cachedOtp = await _cache.GetStringAsync($"otp:{request.Email}:{tenantId}");
        if (cachedOtp == null || cachedOtp != request.Otp)
            throw new InvalidOperationException("Invalid or expired OTP");

        await _cache.RemoveAsync($"otp:{request.Email}:{tenantId}");

        var user = await _db.Users
            .Include(u => u.Profiles)
            .FirstOrDefaultAsync(u => u.Email == request.Email.ToLower() && u.TenantId == tenantId);

        if (user == null)
        {
            // Auto-register via OTP
            user = new User
            {
                TenantId = tenantId,
                Email = request.Email.ToLower(),
                Role = "viewer",
                AuthProvider = "otp",
                IsEmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);

            var profile = new UserProfile
            {
                UserId = user.Id,
                Name = request.Email.Split('@')[0],
                IsDefault = true,
                MaturityLevel = "all"
            };
            _db.UserProfiles.Add(profile);
            await _db.SaveChangesAsync();
        }
        else
        {
            user.IsEmailVerified = true;
            await _db.SaveChangesAsync();
        }

        return await GenerateAuthResponse(user);
    }

    public async Task<AuthResponseDto> SocialLoginAsync(SocialLoginDto request, Guid tenantId)
    {
        string email;
        string? firstName = null, lastName = null, avatarUrl = null, socialId = null;

        switch (request.Provider.ToLower())
        {
            case "google":
                (email, firstName, lastName, avatarUrl, socialId) = await VerifyGoogleTokenAsync(request.Token);
                break;
            case "facebook":
                (email, firstName, lastName, avatarUrl, socialId) = await VerifyFacebookTokenAsync(request.Token);
                break;
            case "apple":
                (email, firstName, lastName, avatarUrl, socialId) = await VerifyAppleTokenAsync(request.Token);
                break;
            default:
                throw new InvalidOperationException($"Unsupported provider: {request.Provider}");
        }

        var user = await _db.Users
            .Include(u => u.Profiles)
            .FirstOrDefaultAsync(u => u.Email == email.ToLower() && u.TenantId == tenantId);

        if (user == null)
        {
            user = new User
            {
                TenantId = tenantId,
                Email = email.ToLower(),
                FirstName = firstName ?? email.Split('@')[0],
                LastName = lastName ?? "",
                AvatarUrl = avatarUrl,
                Role = "viewer",
                AuthProvider = request.Provider.ToLower(),
                SocialId = socialId,
                IsEmailVerified = true,
                CreatedAt = DateTime.UtcNow
            };
            _db.Users.Add(user);

            var profile = new UserProfile
            {
                UserId = user.Id,
                Name = firstName ?? email.Split('@')[0],
                IsDefault = true,
                AvatarUrl = avatarUrl,
                MaturityLevel = "all"
            };
            _db.UserProfiles.Add(profile);
            await _db.SaveChangesAsync();
        }

        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GenerateAuthResponse(user);
    }

    public async Task<bool> LogoutAsync(string refreshToken)
    {
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(r => r.Token == refreshToken);
        if (token == null) return false;

        token.IsRevoked = true;
        token.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ForgotPasswordAsync(string email, Guid tenantId)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLower() && u.TenantId == tenantId);
        if (user == null) return true; // Don't reveal user existence

        var resetToken = Guid.NewGuid().ToString("N");
        await _cache.SetStringAsync($"pwd_reset:{resetToken}", user.Id.ToString(), TimeSpan.FromHours(1));
        await _notificationService.SendPasswordResetAsync(email, user.FirstName, resetToken);
        return true;
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordDto request)
    {
        var userIdStr = await _cache.GetStringAsync($"pwd_reset:{request.Token}");
        if (userIdStr == null)
            throw new InvalidOperationException("Invalid or expired reset token");

        var userId = Guid.Parse(userIdStr);
        var user = await _db.Users.FindAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");

        user.PasswordHash = BC.HashPassword(request.NewPassword);
        await _cache.RemoveAsync($"pwd_reset:{request.Token}");

        // Revoke all refresh tokens
        var tokens = await _db.RefreshTokens.Where(r => r.UserId == userId && !r.IsRevoked).ToListAsync();
        tokens.ForEach(t => { t.IsRevoked = true; t.RevokedAt = DateTime.UtcNow; });

        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordDto request)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) throw new InvalidOperationException("User not found");

        if (!BC.Verify(request.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect");

        user.PasswordHash = BC.HashPassword(request.NewPassword);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> VerifyEmailAsync(string token)
    {
        var userIdStr = await _cache.GetStringAsync($"email_verify:{token}");
        if (userIdStr == null) return false;

        var userId = Guid.Parse(userIdStr);
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return false;

        user.IsEmailVerified = true;
        await _cache.RemoveAsync($"email_verify:{token}");
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<AuthResponseDto> SelectProfileAsync(Guid userId, Guid profileId)
    {
        var user = await _db.Users.Include(u => u.Profiles)
            .FirstOrDefaultAsync(u => u.Id == userId && !u.IsDeleted)
            ?? throw new UnauthorizedAccessException("User not found");

        var profile = user.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new KeyNotFoundException("Profile not found");

        // Issue a new access token that carries the selected profile.
        var accessToken = _jwt.GenerateAccessToken(user, profile.Id.ToString());
        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = "",
            ExpiresIn = 3600,
            User = MapUserDto(user),
            Profiles = user.Profiles.Select(MapProfileDto).ToList()
        };
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private async Task<AuthResponseDto> GenerateAuthResponse(User user)
    {
        var accessToken = _jwt.GenerateAccessToken(user);
        var refreshTokenStr = _jwt.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = refreshTokenStr,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedAt = DateTime.UtcNow
        };
        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenStr,
            ExpiresIn = 3600,
            User = MapUserDto(user),
            Profiles = user.Profiles.Select(MapProfileDto).ToList()
        };
    }

    private async Task<(string email, string? firstName, string? lastName, string? avatarUrl, string? socialId)>
        VerifyGoogleTokenAsync(string token)
    {
        using var client = new HttpClient();
        var response = await client.GetFromJsonAsync<GoogleTokenInfo>(
            $"https://oauth2.googleapis.com/tokeninfo?id_token={token}");

        if (response == null || string.IsNullOrEmpty(response.Email))
            throw new UnauthorizedAccessException("Invalid Google token");

        return (response.Email, response.GivenName, response.FamilyName, response.Picture, response.Sub);
    }

    private async Task<(string email, string? firstName, string? lastName, string? avatarUrl, string? socialId)>
        VerifyFacebookTokenAsync(string token)
    {
        using var client = new HttpClient();
        var appToken = $"{_config["Social:Facebook:AppId"]}|{_config["Social:Facebook:AppSecret"]}";
        var response = await client.GetFromJsonAsync<FacebookUserInfo>(
            $"https://graph.facebook.com/me?access_token={token}&fields=id,email,first_name,last_name,picture");

        if (response == null || string.IsNullOrEmpty(response.Email))
            throw new UnauthorizedAccessException("Invalid Facebook token");

        return (response.Email, response.FirstName, response.LastName, response.Picture?.Data?.Url, response.Id);
    }

    private Task<(string email, string? firstName, string? lastName, string? avatarUrl, string? socialId)>
        VerifyAppleTokenAsync(string token)
    {
        // Apple Sign In: decode the identity token (JWT) - simplified
        var parts = token.Split('.');
        if (parts.Length < 2) throw new UnauthorizedAccessException("Invalid Apple token");

        var payload = System.Text.Json.JsonSerializer.Deserialize<AppleTokenPayload>(
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(
                parts[1].PadRight(parts[1].Length + (4 - parts[1].Length % 4) % 4, '='))));

        if (payload == null || string.IsNullOrEmpty(payload.Email))
            throw new UnauthorizedAccessException("Invalid Apple token");

        return Task.FromResult((payload.Email, (string?)null, (string?)null, (string?)null, payload.Sub));
    }

    private static UserDto MapUserDto(User user) => new()
    {
        Id = user.Id,
        Email = user.Email,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Phone = user.Phone,
        AvatarUrl = user.AvatarUrl,
        Role = user.Role,
        IsEmailVerified = user.IsEmailVerified,
        CreatedAt = user.CreatedAt
    };

    private static ProfileDto MapProfileDto(UserProfile p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        AvatarUrl = p.AvatarUrl,
        IsDefault = p.IsDefault,
        MaturityLevel = p.MaturityLevel,
        Language = p.Language
    };
}

// DTO helpers for social auth
record GoogleTokenInfo(string Sub, string Email, string? GivenName, string? FamilyName, string? Picture);
record FacebookUserInfo(string Id, string Email, string? FirstName, string? LastName, FacebookPicture? Picture);
record FacebookPicture(FacebookPictureData? Data);
record FacebookPictureData(string? Url);
record AppleTokenPayload(string Sub, string Email);
