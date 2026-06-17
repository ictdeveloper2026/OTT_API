using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;

namespace OTT.API.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth)
    {
        _auth = auth;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto req)
    {
        var result = await _auth.RegisterAsync(req, HttpContext.GetTenantId());
        return Ok(result);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto req)
    {
        var result = await _auth.LoginAsync(req, HttpContext.GetTenantId());
        return Ok(result);
    }

    [HttpPost("social")]
    public async Task<IActionResult> SocialLogin([FromBody] SocialLoginDto req)
    {
        var result = await _auth.SocialLoginAsync(req, HttpContext.GetTenantId());
        return Ok(result);
    }

    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpRequestDto req)
    {
        await _auth.SendOtpAsync(req.Email, HttpContext.GetTenantId());
        return Ok(new { message = "OTP sent to email" });
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto req)
    {
        var result = await _auth.VerifyOtpAsync(req, HttpContext.GetTenantId());
        return Ok(result);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequestDto req)
    {
        var result = await _auth.RefreshTokenAsync(req.RefreshToken);
        return Ok(result);
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto req)
    {
        await _auth.ForgotPasswordAsync(req.Email, HttpContext.GetTenantId());
        // Always return OK to prevent email enumeration
        return Ok(new { message = "If that email exists, a reset link was sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto req)
    {
        await _auth.ResetPasswordAsync(req);
        return Ok(new { message = "Password updated" });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto req)
    {
        await _auth.ChangePasswordAsync(HttpContext.RequireUserId(), req);
        return Ok(new { message = "Password changed" });
    }

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string token)
    {
        var ok = await _auth.VerifyEmailAsync(token);
        return ok ? Ok(new { message = "Email verified" }) : BadRequest(new { error = "Invalid or expired token" });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] LogoutRequestDto req)
    {
        await _auth.LogoutAsync(req.RefreshToken);
        return Ok(new { message = "Logged out" });
    }
}
