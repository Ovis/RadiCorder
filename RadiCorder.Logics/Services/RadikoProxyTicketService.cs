using System.Collections.Concurrent;

namespace RadiCorder.Logics.Services;

/// <summary>
/// radiko proxy 用の短命キーを管理する
/// </summary>
public class RadikoProxyTicketService : IRadikoProxyTicketService
{
    private static readonly TimeSpan TicketLifetime = TimeSpan.FromHours(12);
    private readonly ConcurrentDictionary<string, TicketEntry> _tickets = new(StringComparer.Ordinal);

    /// <summary>
    /// 認証トークンに対応する短命キーを発行する
    /// </summary>
    public string IssueTokenTicket(string token)
    {
        PurgeExpiredTickets();

        var ticket = Ulid.NewUlid().ToString();
        _tickets[ticket] = new TicketEntry(token, DateTimeOffset.UtcNow.Add(TicketLifetime));
        return ticket;
    }

    /// <summary>
    /// 短命キーから認証トークンを解決する
    /// </summary>
    public bool TryGetToken(string ticket, out string token)
    {
        token = string.Empty;
        if (!_tickets.TryGetValue(ticket, out var entry))
        {
            return false;
        }

        if (entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _tickets.TryRemove(ticket, out _);
            return false;
        }

        token = entry.Token;
        return true;
    }

    private void PurgeExpiredTickets()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _tickets)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                _tickets.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record TicketEntry(string Token, DateTimeOffset ExpiresAtUtc);
}
