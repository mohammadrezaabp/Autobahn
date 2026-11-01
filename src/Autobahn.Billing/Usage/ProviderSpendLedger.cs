namespace Autobahn.Billing.Usage;

/// <summary>
/// Estimated costs reserved by requests in flight at a paid provider, held against two scopes at
/// once: the key and its tenant.
/// </summary>
/// <remarks>
/// The two are checked and taken under one lock, so a request is never admitted under its key's
/// cap and then found to have broken its tenant's. As with the budget ledger, an entry that is
/// never released (the process lost the request) expires after a TTL instead of holding headroom
/// for ever. Outstanding amounts are per process: a second replica would need a shared store.
/// </remarks>
public sealed class ProviderSpendLedger
{
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();
    private readonly Dictionary<string, Reservation> _byRequest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, decimal> _outstanding = new(StringComparer.Ordinal);
    private DateTimeOffset _nextExpiry = DateTimeOffset.MaxValue;

    public ProviderSpendLedger(TimeSpan ttl, Func<DateTimeOffset>? clock = null)
    {
        _ttl = ttl > TimeSpan.Zero ? ttl : TimeSpan.FromSeconds(120);
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public static string TenantScope(Guid tenantId, string provider) => $"t:{tenantId:N}:{provider}";

    public static string KeyScope(Guid apiKeyId, string provider) => $"k:{apiKeyId:N}:{provider}";

    public decimal GetOutstanding(string scope)
    {
        lock (_sync)
        {
            SweepExpired();
            return _outstanding.TryGetValue(scope, out var value) ? value : 0m;
        }
    }

    /// <summary>
    /// Reserves <paramref name="amount"/> in every scope, or in none. A scope with a null headroom
    /// has no cap and always fits; it is still recorded, so a cap set later sees what is in flight.
    /// </summary>
    /// <returns>The scope that did not fit, or null when the reservation was taken.</returns>
    public string? TryReserve(string requestId, decimal amount, IReadOnlyList<(string Scope, decimal? Headroom)> scopes)
    {
        lock (_sync)
        {
            SweepExpired();

            if (_byRequest.ContainsKey(requestId))
            {
                return null;
            }

            foreach (var (scope, headroom) in scopes)
            {
                if (headroom is not { } room)
                {
                    continue;
                }

                var outstanding = _outstanding.TryGetValue(scope, out var existing) ? existing : 0m;

                // An exhausted scope refuses even a request whose cost cannot be estimated.
                if (room - outstanding <= 0m || (amount > 0m && outstanding + amount > room))
                {
                    return scope;
                }
            }

            var expiresAt = _clock() + _ttl;
            _byRequest[requestId] = new Reservation(scopes.Select(s => s.Scope).ToArray(), amount, expiresAt);
            if (expiresAt < _nextExpiry)
            {
                _nextExpiry = expiresAt;
            }

            if (amount > 0m)
            {
                foreach (var (scope, _) in scopes)
                {
                    _outstanding[scope] = (_outstanding.TryGetValue(scope, out var existing) ? existing : 0m) + amount;
                }
            }

            return null;
        }
    }

    public void Release(string requestId)
    {
        lock (_sync)
        {
            if (_byRequest.Remove(requestId, out var reservation))
            {
                Subtract(reservation);
            }

            if (_byRequest.Count == 0)
            {
                _nextExpiry = DateTimeOffset.MaxValue;
            }
        }
    }

    private void SweepExpired()
    {
        var now = _clock();
        if (now < _nextExpiry)
        {
            return;
        }

        var next = DateTimeOffset.MaxValue;
        List<string>? expired = null;
        foreach (var (requestId, reservation) in _byRequest)
        {
            if (reservation.ExpiresAt <= now)
            {
                (expired ??= []).Add(requestId);
            }
            else if (reservation.ExpiresAt < next)
            {
                next = reservation.ExpiresAt;
            }
        }

        _nextExpiry = next;
        if (expired is null)
        {
            return;
        }

        foreach (var requestId in expired)
        {
            if (_byRequest.Remove(requestId, out var reservation))
            {
                Subtract(reservation);
            }
        }
    }

    private void Subtract(Reservation reservation)
    {
        if (reservation.Amount <= 0m)
        {
            return;
        }

        foreach (var scope in reservation.Scopes)
        {
            if (!_outstanding.TryGetValue(scope, out var current))
            {
                continue;
            }

            var next = current - reservation.Amount;
            if (next <= 0m)
            {
                _outstanding.Remove(scope);
            }
            else
            {
                _outstanding[scope] = next;
            }
        }
    }

    private readonly record struct Reservation(string[] Scopes, decimal Amount, DateTimeOffset ExpiresAt);
}
