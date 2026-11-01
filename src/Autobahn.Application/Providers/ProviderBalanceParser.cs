using System.Collections.Concurrent;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Providers;
/// <summary>Reads the credit figures out of a provider's balance response.</summary>
public static class ProviderBalanceParser
{
    /// <summary>
    /// OpenRouter <c>GET /api/v1/key</c>: the key's own usage and limit. A key without a limit
    /// reports <c>limit: null</c>, and the remaining credit is then the account's, read separately.
    /// </summary>
    public static bool TryParseOpenRouterKey(System.Text.Json.JsonElement root, out ProviderBalanceReading reading, out bool hasLimit)
    {
        reading = null!;
        hasLimit = false;
        if (!TryData(root, out var data))
        {
            return false;
        }

        var used = Number(data, "usage");
        var limit = Number(data, "limit");
        var remaining = Number(data, "limit_remaining");
        hasLimit = limit is not null;
        if (limit is not null && remaining is null && used is not null)
        {
            remaining = limit - used;
        }

        reading = new ProviderBalanceReading(remaining, limit, used, "USD", DateTimeOffset.UtcNow, null);
        return used is not null || limit is not null;
    }

    /// <summary>OpenRouter <c>GET /api/v1/credits</c>: credits bought and credits used, for the account.</summary>
    public static bool TryParseOpenRouterCredits(System.Text.Json.JsonElement root, out ProviderBalanceReading reading)
    {
        reading = null!;
        if (!TryData(root, out var data))
        {
            return false;
        }

        var total = Number(data, "total_credits");
        var used = Number(data, "total_usage");
        if (total is null || used is null)
        {
            return false;
        }

        reading = new ProviderBalanceReading(total - used, total, used, "USD", DateTimeOffset.UtcNow, null);
        return true;
    }

    /// <summary>
    /// DeepSeek <c>GET /user/balance</c>: one entry per currency, amounts as strings. The USD
    /// entry is preferred; an account topped up in another currency is reported in that currency.
    /// </summary>
    public static bool TryParseDeepSeek(System.Text.Json.JsonElement root, out ProviderBalanceReading reading)
    {
        reading = null!;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("balance_infos", out var infos) || infos.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        ProviderBalanceReading? first = null;
        foreach (var info in infos.EnumerateArray())
        {
            if (info.ValueKind != System.Text.Json.JsonValueKind.Object || Number(info, "total_balance")is not { } balance)
            {
                continue;
            }

            var currency = info.TryGetProperty("currency", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String ? c.GetString() ?? "USD" : "USD";
            var parsed = new ProviderBalanceReading(balance, null, null, currency.ToUpperInvariant(), DateTimeOffset.UtcNow, null);
            if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            {
                reading = parsed;
                return true;
            }

            first ??= parsed;
        }

        if (first is null)
        {
            return false;
        }

        reading = first;
        return true;
    }

    /// <summary>
    /// One page of OpenAI <c>GET /v1/organization/costs</c>: daily buckets, each with results whose
    /// <c>amount.value</c> is USD. <paramref name = "nextPage"/> is the cursor of the next page, if any.
    /// </summary>
    public static bool TryParseOpenAiCostsPage(System.Text.Json.JsonElement root, out decimal used, out string? nextPage)
    {
        used = 0m;
        nextPage = null;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("data", out var buckets) || buckets.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return false;
        }

        foreach (var bucket in buckets.EnumerateArray())
        {
            if (bucket.ValueKind != System.Text.Json.JsonValueKind.Object || !bucket.TryGetProperty("results", out var results) || results.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                if (result.ValueKind == System.Text.Json.JsonValueKind.Object && result.TryGetProperty("amount", out var amount) && amount.ValueKind == System.Text.Json.JsonValueKind.Object && Number(amount, "value")is { } value)
                {
                    used += value;
                }
            }
        }

        if (root.TryGetProperty("has_more", out var hasMore) && hasMore.ValueKind == System.Text.Json.JsonValueKind.True && root.TryGetProperty("next_page", out var next) && next.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            nextPage = next.GetString();
        }

        return true;
    }

    /// <summary>Deepgram <c>GET /v1/projects</c>: the ids of the projects the key can see.</summary>
    public static IReadOnlyList<string> DeepgramProjectIds(System.Text.Json.JsonElement root)
    {
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("projects", out var projects) || projects.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            return[];
        }

        return projects.EnumerateArray().Where(p => p.ValueKind == System.Text.Json.JsonValueKind.Object).Select(p => p.TryGetProperty("project_id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String ? id.GetString() : null).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).ToList();
    }

    /// <summary>
    /// Deepgram <c>GET /v1/projects/{id}/balances</c>, one response per project: every balance
    /// whose <c>units</c> is USD, summed.
    /// </summary>
    public static bool TryParseDeepgramBalances(IEnumerable<System.Text.Json.JsonElement> roots, out ProviderBalanceReading reading)
    {
        reading = null!;
        decimal? remaining = null;
        foreach (var root in roots)
        {
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.TryGetProperty("balances", out var balances) || balances.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                continue;
            }

            foreach (var balance in balances.EnumerateArray())
            {
                if (balance.ValueKind != System.Text.Json.JsonValueKind.Object || Number(balance, "amount")is not { } amount)
                {
                    continue;
                }

                var units = balance.TryGetProperty("units", out var u) && u.ValueKind == System.Text.Json.JsonValueKind.String ? u.GetString() : "usd";
                if (string.Equals(units, "usd", StringComparison.OrdinalIgnoreCase))
                {
                    remaining = (remaining ?? 0m) + amount;
                }
            }
        }

        if (remaining is null)
        {
            return false;
        }

        reading = new ProviderBalanceReading(remaining, null, null, "USD", DateTimeOffset.UtcNow, null);
        return true;
    }

    /// <summary>
    /// ElevenLabs <c>GET /v1/user/subscription</c>: credits used this period
    /// (<c>character_count</c>) of the period's allowance (<c>character_limit</c>). The figures are
    /// credits, not money, so they are reported in <see cref = "ElevenLabsCreditUnit"/>.
    /// </summary>
    public static bool TryParseElevenLabsSubscription(System.Text.Json.JsonElement root, out ProviderBalanceReading reading)
    {
        reading = null!;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object || Number(root, "character_count")is not { } used || Number(root, "character_limit")is not { } limit)
        {
            return false;
        }

        reading = new ProviderBalanceReading(limit - used, limit, used, ElevenLabsCreditUnit, DateTimeOffset.UtcNow, null);
        return true;
    }

    /// <summary>The unit ElevenLabs balances are in. Not a currency, so reconciliation leaves them out.</summary>
    public const string ElevenLabsCreditUnit = "CREDITS";
    private static bool TryData(System.Text.Json.JsonElement root, out System.Text.Json.JsonElement data)
    {
        data = default;
        return root.ValueKind == System.Text.Json.JsonValueKind.Object && root.TryGetProperty("data", out data) && data.ValueKind == System.Text.Json.JsonValueKind.Object;
    }

    /// <summary>A number, or a string holding one; anything else is "not reported".</summary>
    private static decimal? Number(System.Text.Json.JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var element))
        {
            return null;
        }

        if (element.ValueKind == System.Text.Json.JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        return element.ValueKind == System.Text.Json.JsonValueKind.String && decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
