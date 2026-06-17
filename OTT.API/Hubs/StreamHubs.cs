using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OTT.Application.Services;
using OTT.Infrastructure.Data;
using OTT.Infrastructure.Services;
using System.Security.Claims;

namespace OTT.API.Hubs;

// ── Watch Party Hub ────────────────────────────────────────────────────────────

[Authorize]
public class WatchPartyHub : Hub
{
    private readonly OttDbContext _db;
    private readonly IRedisCacheService _cache;
    private readonly ILogger<WatchPartyHub> _logger;

    public WatchPartyHub(OttDbContext db, IRedisCacheService cache, ILogger<WatchPartyHub> logger)
    {
        _db = db;
        _cache = cache;
        _logger = logger;
    }

    public async Task JoinParty(string partyCode)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var party = await _db.WatchParties
            .Include(p => p.Members)
            .FirstOrDefaultAsync(p => p.Code == partyCode && p.Status == "active");

        if (party == null)
        {
            await Clients.Caller.SendAsync("Error", "Party not found or has ended");
            return;
        }

        if (party.Members.Count >= party.MaxMembers && !party.Members.Any(m => m.UserId == userId))
        {
            await Clients.Caller.SendAsync("Error", "Party is full");
            return;
        }

        // Add member if not already in
        if (!party.Members.Any(m => m.UserId == userId))
        {
            party.Members.Add(new Domain.Entities.WatchPartyMember
            {
                WatchPartyId = party.Id,
                UserId = userId,
                JoinedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, partyCode);

        // Send current playback state
        var state = await _cache.GetStringAsync($"party_state:{party.Id}");
        if (state != null)
            await Clients.Caller.SendAsync("SyncState", state);

        var user = await _db.Users.FindAsync(userId);
        await Clients.Group(partyCode).SendAsync("UserJoined", new
        {
            userId,
            userName = $"{user?.FirstName} {user?.LastName}".Trim(),
            avatarUrl = user?.AvatarUrl
        });
    }

    public async Task LeaveParty(string partyCode)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, partyCode);

        var user = await _db.Users.FindAsync(userId);
        await Clients.Group(partyCode).SendAsync("UserLeft", new { userId, userName = $"{user?.FirstName} {user?.LastName}".Trim() });
    }

    public async Task SendPlaybackEvent(string partyCode, string eventType, double positionSeconds)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // Only host can control playback
        var party = await _db.WatchParties.FirstOrDefaultAsync(p => p.Code == partyCode);
        if (party == null || party.HostUserId != userId) return;

        var state = System.Text.Json.JsonSerializer.Serialize(new
        {
            eventType,
            positionSeconds,
            isPlaying = eventType == "play",
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });

        await _cache.SetStringAsync($"party_state:{party.Id}", state, TimeSpan.FromHours(4));

        // Broadcast to all members
        await Clients.Group(partyCode).SendAsync("PlaybackEvent", new { eventType, positionSeconds, senderId = userId });
    }

    public async Task SendChatMessage(string partyCode, string message)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var user = await _db.Users.FindAsync(userId);

        await Clients.Group(partyCode).SendAsync("ChatMessage", new
        {
            userId,
            userName = $"{user?.FirstName} {user?.LastName}".Trim(),
            avatarUrl = user?.AvatarUrl,
            message = message.Length > 200 ? message[..200] : message,
            timestamp = DateTime.UtcNow
        });
    }

    public async Task SendReaction(string partyCode, string reaction)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await Clients.Group(partyCode).SendAsync("Reaction", new { userId, reaction });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // User will be cleaned up naturally; optionally notify groups
        await base.OnDisconnectedAsync(exception);
    }
}

// ── Live Chat Hub ─────────────────────────────────────────────────────────────

[Authorize]
public class LiveChatHub : Hub
{
    private readonly OttDbContext _db;
    private readonly ILiveStreamService _liveService;
    private readonly IRedisCacheService _cache;
    private readonly ILogger<LiveChatHub> _logger;

    public LiveChatHub(OttDbContext db, ILiveStreamService liveService, IRedisCacheService cache, ILogger<LiveChatHub> logger)
    {
        _db = db;
        _liveService = liveService;
        _cache = cache;
        _logger = logger;
    }

    public async Task JoinStream(string streamId)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        if (!Guid.TryParse(streamId, out var streamGuid)) return;

        await Groups.AddToGroupAsync(Context.ConnectionId, streamId);
        await _liveService.UpdateViewerCountAsync(streamGuid, 1);

        var viewerCount = await _cache.GetStringAsync($"live_viewers:{streamId}");
        await Clients.Group(streamId).SendAsync("ViewerCount", int.Parse(viewerCount ?? "0"));
    }

    public async Task LeaveStream(string streamId)
    {
        if (!Guid.TryParse(streamId, out var streamGuid)) return;

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, streamId);
        await _liveService.UpdateViewerCountAsync(streamGuid, -1);

        var viewerCount = await _cache.GetStringAsync($"live_viewers:{streamId}");
        await Clients.Group(streamId).SendAsync("ViewerCount", int.Parse(viewerCount ?? "0"));
    }

    public async Task SendMessage(string streamId, string message)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);

        // Rate limit: max 1 message per 2 seconds per user
        var rateKey = $"chat_rate:{streamId}:{userId}";
        var count = await _cache.IncrementAsync(rateKey, TimeSpan.FromSeconds(2));
        if (count > 2) return;

        // Check for banned words (simple approach)
        if (await IsSpamAsync(message)) return;

        if (!Guid.TryParse(streamId, out var streamGuid)) return;

        await _liveService.SendChatMessageAsync(streamGuid, userId, message);

        var user = await _db.Users.FindAsync(userId);
        await Clients.Group(streamId).SendAsync("NewMessage", new
        {
            userId,
            userName = $"{user?.FirstName} {user?.LastName}".Trim(),
            avatarUrl = user?.AvatarUrl,
            message,
            timestamp = DateTime.UtcNow,
            isModerator = user?.Role == "admin" || user?.Role == "moderator"
        });
    }

    public async Task SendReaction(string streamId, string emoji)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var validEmojis = new[] { "❤️", "😂", "😮", "🎉", "👏", "🔥" };
        if (!validEmojis.Contains(emoji)) return;

        await Clients.Group(streamId).SendAsync("Reaction", new { userId, emoji });
    }

    private static Task<bool> IsSpamAsync(string message)
    {
        // Basic spam check - extend with ML in production
        if (message.Length < 1 || string.IsNullOrWhiteSpace(message)) return Task.FromResult(true);
        return Task.FromResult(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}

// ── Notification Hub ─────────────────────────────────────────────────────────

[Authorize]
public class NotificationHub : Hub
{
    private readonly OttDbContext _db;
    private readonly ILogger<NotificationHub> _logger;
    private static readonly Dictionary<string, string> _userConnections = new();

    public NotificationHub(OttDbContext db, ILogger<NotificationHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            _userConnections[userId] = Context.ConnectionId;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");

            // Send unread count
            var unreadCount = await _db.Notifications
                .CountAsync(n => n.UserId == Guid.Parse(userId) && !n.IsRead);
            await Clients.Caller.SendAsync("UnreadCount", unreadCount);
        }
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            _userConnections.Remove(userId);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task MarkAsRead(Guid notificationId)
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

        if (notification != null)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            var unreadCount = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
            await Clients.Caller.SendAsync("UnreadCount", unreadCount);
        }
    }

    public async Task MarkAllAsRead()
    {
        var userId = Guid.Parse(Context.User!.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var notifications = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        var now = DateTime.UtcNow;
        notifications.ForEach(n => { n.IsRead = true; n.ReadAt = now; });
        await _db.SaveChangesAsync();
        await Clients.Caller.SendAsync("UnreadCount", 0);
    }
}

// ── Hub Helpers (static service for sending from controllers) ─────────────────

public interface IHubService
{
    Task SendNotificationToUserAsync(Guid userId, string title, string body, object? data = null);
    Task BroadcastToTenantAsync(Guid tenantId, string eventName, object payload);
}

public class HubService : IHubService
{
    private readonly IHubContext<NotificationHub> _notificationHub;

    public HubService(IHubContext<NotificationHub> notificationHub)
    {
        _notificationHub = notificationHub;
    }

    public async Task SendNotificationToUserAsync(Guid userId, string title, string body, object? data = null)
    {
        await _notificationHub.Clients.Group($"user_{userId}").SendAsync("Notification", new
        {
            title,
            body,
            data,
            timestamp = DateTime.UtcNow
        });
    }

    public async Task BroadcastToTenantAsync(Guid tenantId, string eventName, object payload)
    {
        await _notificationHub.Clients.Group($"tenant_{tenantId}").SendAsync(eventName, payload);
    }
}
