using Microsoft.EntityFrameworkCore;
using OTT.Application.DTOs;
using OTT.Domain.Entities;
using OTT.Infrastructure.Data;

namespace OTT.Application.Services;

/// <summary>
/// Community feature: users submit content suggestions and upvote them; admins
/// promote popular suggestions into polls the community votes on. All operations
/// are tenant-scoped. Vote counters are recomputed from the vote tables after each
/// change so concurrent votes never drift.
/// </summary>
public interface ICommunityService
{
    // Suggestions (user)
    Task<List<SuggestionDto>> GetSuggestionsAsync(Guid tenantId, Guid userId, string sort, string? status, int page, int pageSize);
    Task<SuggestionDto> CreateSuggestionAsync(Guid tenantId, Guid userId, CreateSuggestionDto dto);
    Task<SuggestionDto> ToggleUpvoteAsync(Guid tenantId, Guid userId, Guid suggestionId);

    // Polls (user)
    Task<List<PollDto>> GetPollsAsync(Guid tenantId, Guid userId, string? status);
    Task<PollDto> GetPollAsync(Guid tenantId, Guid userId, Guid pollId);
    Task<PollDto> VoteAsync(Guid tenantId, Guid userId, Guid pollId, Guid optionId);

    // Admin
    Task<List<SuggestionDto>> GetAdminSuggestionsAsync(Guid tenantId, string? status);
    Task<SuggestionDto> UpdateSuggestionStatusAsync(Guid tenantId, Guid suggestionId, UpdateSuggestionStatusDto dto);
    Task DeleteSuggestionAsync(Guid tenantId, Guid suggestionId);
    Task<PollDto> CreatePollAsync(Guid tenantId, Guid userId, CreatePollDto dto);
    Task<PollDto> PromoteSuggestionAsync(Guid tenantId, Guid userId, Guid suggestionId, CreatePollDto dto);
    Task<PollDto> UpdatePollAsync(Guid tenantId, Guid pollId, UpdatePollDto dto);
    Task DeletePollAsync(Guid tenantId, Guid pollId);
}

public class CommunityService : ICommunityService
{
    private readonly OttDbContext _db;
    public CommunityService(OttDbContext db) => _db = db;

    // ── Suggestions ────────────────────────────────────────────────────────────
    public async Task<List<SuggestionDto>> GetSuggestionsAsync(Guid tenantId, Guid userId, string sort, string? status, int page, int pageSize)
    {
        var query = _db.Suggestions.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);

        query = sort == "new"
            ? query.OrderByDescending(s => s.CreatedAt)
            : query.OrderByDescending(s => s.UpvoteCount).ThenByDescending(s => s.CreatedAt);

        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = items.Select(s => s.Id).ToList();
        var voted = await _db.SuggestionVotes.AsNoTracking()
            .Where(v => v.UserId == userId && ids.Contains(v.SuggestionId))
            .Select(v => v.SuggestionId).ToListAsync();
        var votedSet = voted.ToHashSet();
        return items.Select(s => MapSuggestion(s, votedSet.Contains(s.Id))).ToList();
    }

    public async Task<SuggestionDto> CreateSuggestionAsync(Guid tenantId, Guid userId, CreateSuggestionDto dto)
    {
        var s = new Suggestion
        {
            TenantId = tenantId,
            CreatedByUserId = userId,
            Title = dto.Title.Trim(),
            Description = dto.Description?.Trim(),
            Status = "open"
        };
        _db.Suggestions.Add(s);
        await _db.SaveChangesAsync();
        return MapSuggestion(s, false);
    }

    public async Task<SuggestionDto> ToggleUpvoteAsync(Guid tenantId, Guid userId, Guid suggestionId)
    {
        var s = await _db.Suggestions.FirstOrDefaultAsync(x => x.Id == suggestionId && x.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Suggestion not found");

        var existing = await _db.SuggestionVotes.FirstOrDefaultAsync(v => v.SuggestionId == suggestionId && v.UserId == userId);
        bool hasVoted;
        if (existing != null) { _db.SuggestionVotes.Remove(existing); hasVoted = false; }
        else { _db.SuggestionVotes.Add(new SuggestionVote { SuggestionId = suggestionId, UserId = userId }); hasVoted = true; }
        await _db.SaveChangesAsync();

        s.UpvoteCount = await _db.SuggestionVotes.CountAsync(v => v.SuggestionId == suggestionId);
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return MapSuggestion(s, hasVoted);
    }

    // ── Polls ──────────────────────────────────────────────────────────────────
    public async Task<List<PollDto>> GetPollsAsync(Guid tenantId, Guid userId, string? status)
    {
        var query = _db.Polls.AsNoTracking().Include(p => p.Options).Where(p => p.TenantId == tenantId);
        var now = DateTime.UtcNow;
        if (status == "active") query = query.Where(p => p.Status == "open" && (p.EndsAt == null || p.EndsAt > now));
        else if (status == "closed") query = query.Where(p => p.Status == "closed" || (p.EndsAt != null && p.EndsAt <= now));

        var polls = await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
        var ids = polls.Select(p => p.Id).ToList();
        var myVotes = await _db.PollVotes.AsNoTracking()
            .Where(v => v.UserId == userId && ids.Contains(v.PollId))
            .ToDictionaryAsync(v => v.PollId, v => v.PollOptionId);
        return polls.Select(p => MapPoll(p, myVotes.TryGetValue(p.Id, out var opt) ? opt : (Guid?)null)).ToList();
    }

    public async Task<PollDto> GetPollAsync(Guid tenantId, Guid userId, Guid pollId)
    {
        var p = await _db.Polls.AsNoTracking().Include(x => x.Options)
            .FirstOrDefaultAsync(x => x.Id == pollId && x.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Poll not found");
        var my = await _db.PollVotes.AsNoTracking().FirstOrDefaultAsync(v => v.PollId == pollId && v.UserId == userId);
        return MapPoll(p, my?.PollOptionId);
    }

    public async Task<PollDto> VoteAsync(Guid tenantId, Guid userId, Guid pollId, Guid optionId)
    {
        var poll = await _db.Polls.Include(p => p.Options)
            .FirstOrDefaultAsync(p => p.Id == pollId && p.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Poll not found");
        if (IsClosed(poll)) throw new InvalidOperationException("This poll is closed.");
        if (poll.Options.All(o => o.Id != optionId)) throw new KeyNotFoundException("Option not found");

        var existing = await _db.PollVotes.FirstOrDefaultAsync(v => v.PollId == pollId && v.UserId == userId);
        if (existing == null)
            _db.PollVotes.Add(new PollVote { PollId = pollId, PollOptionId = optionId, UserId = userId });
        else
            existing.PollOptionId = optionId; // change vote (allowed until close)
        await _db.SaveChangesAsync();

        await RecomputePollCountsAsync(poll);
        return MapPoll(poll, optionId);
    }

    // ── Admin ──────────────────────────────────────────────────────────────────
    public async Task<List<SuggestionDto>> GetAdminSuggestionsAsync(Guid tenantId, string? status)
    {
        var query = _db.Suggestions.AsNoTracking().Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);
        var items = await query.OrderByDescending(s => s.UpvoteCount).ThenByDescending(s => s.CreatedAt).ToListAsync();
        return items.Select(s => MapSuggestion(s, false)).ToList();
    }

    public async Task<SuggestionDto> UpdateSuggestionStatusAsync(Guid tenantId, Guid suggestionId, UpdateSuggestionStatusDto dto)
    {
        var s = await _db.Suggestions.FirstOrDefaultAsync(x => x.Id == suggestionId && x.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Suggestion not found");
        s.Status = dto.Status;
        s.LinkedContentId = dto.LinkedContentId ?? s.LinkedContentId;
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return MapSuggestion(s, false);
    }

    public async Task DeleteSuggestionAsync(Guid tenantId, Guid suggestionId)
    {
        var s = await _db.Suggestions.FirstOrDefaultAsync(x => x.Id == suggestionId && x.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Suggestion not found");
        // Cascade delete is disabled globally; remove children explicitly.
        await _db.SuggestionVotes.Where(v => v.SuggestionId == suggestionId).ExecuteDeleteAsync();
        _db.Suggestions.Remove(s);
        await _db.SaveChangesAsync();
    }

    public async Task<PollDto> CreatePollAsync(Guid tenantId, Guid userId, CreatePollDto dto)
    {
        var poll = BuildPoll(tenantId, userId, dto.Question, dto.Description, dto.EndsAt, dto.Options, dto.SourceSuggestionId);
        _db.Polls.Add(poll);
        await _db.SaveChangesAsync();
        return MapPoll(poll, null);
    }

    public async Task<PollDto> PromoteSuggestionAsync(Guid tenantId, Guid userId, Guid suggestionId, CreatePollDto dto)
    {
        var s = await _db.Suggestions.FirstOrDefaultAsync(x => x.Id == suggestionId && x.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Suggestion not found");

        var question = string.IsNullOrWhiteSpace(dto.Question) ? s.Title : dto.Question;
        var options = dto.Options.Count >= 2 ? dto.Options : new List<string> { "Yes, add this", "No" };
        var poll = BuildPoll(tenantId, userId, question, dto.Description ?? s.Description, dto.EndsAt, options, suggestionId);
        _db.Polls.Add(poll);

        s.Status = "promoted";
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return MapPoll(poll, null);
    }

    public async Task<PollDto> UpdatePollAsync(Guid tenantId, Guid pollId, UpdatePollDto dto)
    {
        var poll = await _db.Polls.Include(p => p.Options)
            .FirstOrDefaultAsync(p => p.Id == pollId && p.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Poll not found");
        if (!string.IsNullOrWhiteSpace(dto.Question)) poll.Question = dto.Question;
        if (dto.Description != null) poll.Description = dto.Description;
        if (!string.IsNullOrWhiteSpace(dto.Status)) poll.Status = dto.Status;
        if (dto.EndsAt.HasValue) poll.EndsAt = dto.EndsAt;
        await _db.SaveChangesAsync();
        return MapPoll(poll, null);
    }

    public async Task DeletePollAsync(Guid tenantId, Guid pollId)
    {
        var poll = await _db.Polls.FirstOrDefaultAsync(p => p.Id == pollId && p.TenantId == tenantId)
            ?? throw new KeyNotFoundException("Poll not found");
        await _db.PollVotes.Where(v => v.PollId == pollId).ExecuteDeleteAsync();
        await _db.PollOptions.Where(o => o.PollId == pollId).ExecuteDeleteAsync();
        _db.Polls.Remove(poll);
        await _db.SaveChangesAsync();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    private static bool IsClosed(Poll p) => p.Status == "closed" || (p.EndsAt.HasValue && p.EndsAt.Value <= DateTime.UtcNow);

    private static Poll BuildPoll(Guid tenantId, Guid userId, string question, string? description, DateTime? endsAt, List<string> options, Guid? sourceSuggestionId)
    {
        var poll = new Poll
        {
            TenantId = tenantId,
            CreatedByUserId = userId,
            Question = question.Trim(),
            Description = description?.Trim(),
            EndsAt = endsAt,
            SourceSuggestionId = sourceSuggestionId,
            Status = "open"
        };
        var order = 0;
        foreach (var text in options.Where(o => !string.IsNullOrWhiteSpace(o)))
            poll.Options.Add(new PollOption { Text = text.Trim(), SortOrder = order++ });
        return poll;
    }

    private async Task RecomputePollCountsAsync(Poll poll)
    {
        var counts = await _db.PollVotes.Where(v => v.PollId == poll.Id)
            .GroupBy(v => v.PollOptionId)
            .Select(g => new { OptionId = g.Key, Count = g.Count() })
            .ToListAsync();
        foreach (var opt in poll.Options)
            opt.VoteCount = counts.FirstOrDefault(c => c.OptionId == opt.Id)?.Count ?? 0;
        await _db.SaveChangesAsync();
    }

    private static SuggestionDto MapSuggestion(Suggestion s, bool hasVoted) => new()
    {
        Id = s.Id,
        Title = s.Title,
        Description = s.Description,
        Status = s.Status,
        UpvoteCount = s.UpvoteCount,
        HasVoted = hasVoted,
        LinkedContentId = s.LinkedContentId,
        CreatedByUserId = s.CreatedByUserId,
        CreatedAt = s.CreatedAt
    };

    private static PollDto MapPoll(Poll p, Guid? myOptionId) => new()
    {
        Id = p.Id,
        Question = p.Question,
        Description = p.Description,
        Status = IsClosed(p) ? "closed" : "open",
        EndsAt = p.EndsAt,
        CreatedAt = p.CreatedAt,
        TotalVotes = p.Options.Sum(o => o.VoteCount),
        MyOptionId = myOptionId,
        Options = p.Options.OrderBy(o => o.SortOrder)
            .Select(o => new PollOptionDto { Id = o.Id, Text = o.Text, VoteCount = o.VoteCount, LinkedContentId = o.LinkedContentId, SortOrder = o.SortOrder })
            .ToList()
    };
}
