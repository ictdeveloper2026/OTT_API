using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OTT.API.Middleware;
using OTT.Application.DTOs;
using OTT.Application.Services;

namespace OTT.API.Controllers;

// User-facing community endpoints: browse/submit/upvote suggestions and vote in polls.
[ApiController]
[Route("api")]
[Authorize]
public class CommunityController : ControllerBase
{
    private readonly ICommunityService _community;
    public CommunityController(ICommunityService community) => _community = community;

    // ── Suggestions ──
    [HttpGet("suggestions")]
    public async Task<IActionResult> GetSuggestions([FromQuery] string sort = "top", [FromQuery] string? status = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await _community.GetSuggestionsAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(),
            sort, status, page, Math.Clamp(pageSize, 1, 100));
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpPost("suggestions")]
    [EnableRateLimiting("auth")] // abuse-prone create path
    public async Task<IActionResult> CreateSuggestion([FromBody] CreateSuggestionDto dto)
        => Ok(ApiResponse<object>.Ok(await _community.CreateSuggestionAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(), dto)));

    [HttpPost("suggestions/{id:guid}/upvote")]
    public async Task<IActionResult> ToggleUpvote(Guid id)
        => Ok(ApiResponse<object>.Ok(await _community.ToggleUpvoteAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(), id)));

    // ── Polls ──
    [HttpGet("polls")]
    public async Task<IActionResult> GetPolls([FromQuery] string? status = "active")
        => Ok(ApiResponse<object>.Ok(await _community.GetPollsAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(), status)));

    [HttpGet("polls/{id:guid}")]
    public async Task<IActionResult> GetPoll(Guid id)
        => Ok(ApiResponse<object>.Ok(await _community.GetPollAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(), id)));

    [HttpPost("polls/{id:guid}/vote")]
    public async Task<IActionResult> Vote(Guid id, [FromBody] CastVoteDto dto)
        => Ok(ApiResponse<object>.Ok(await _community.VoteAsync(HttpContext.GetTenantId(), HttpContext.RequireUserId(), id, dto.OptionId)));
}
