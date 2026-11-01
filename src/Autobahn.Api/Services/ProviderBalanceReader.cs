using System.Net.Http.Headers;
using System.Text.Json;
using Autobahn.Domain.Models;
using Autobahn.Application.Providers;

namespace Autobahn.Api.Services;

/// <summary>
/// Reads a paid provider's remaining credit with the company key. These are account endpoints,
/// not inference: they cost nothing.
/// </summary>
public sealed class ProviderBalanceReader(HttpClient http)
{
    public async Task<ProviderBalanceReading> ReadAsync(
        ModelProviderDefinition provider,
        string apiKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var baseUrl = provider.UpstreamBaseUrl.TrimEnd('/');

        switch (provider.Balance)
        {
            case ProviderBalanceKind.OpenRouter:
            {
                using var key = await GetAsync($"{baseUrl}/v1/key", apiKey, cancellationToken).ConfigureAwait(false);
                if (!ProviderBalanceParser.TryParseOpenRouterKey(key.RootElement, out var reading, out var hasLimit))
                {
                    throw new InvalidOperationException("The provider's key endpoint returned no usage figures.");
                }

                if (hasLimit)
                {
                    return reading;
                }

                // A key without a limit of its own spends the account's credits.
                using var credits = await GetAsync($"{baseUrl}/v1/credits", apiKey, cancellationToken).ConfigureAwait(false);
                return ProviderBalanceParser.TryParseOpenRouterCredits(credits.RootElement, out var account)
                    ? account
                    : reading;
            }

            case ProviderBalanceKind.DeepSeek:
            {
                using var balance = await GetAsync($"{baseUrl}/user/balance", apiKey, cancellationToken).ConfigureAwait(false);
                return ProviderBalanceParser.TryParseDeepSeek(balance.RootElement, out var reading)
                    ? reading
                    : throw new InvalidOperationException("The provider's balance endpoint returned no balance.");
            }

            case ProviderBalanceKind.OpenAi:
                return await ReadOpenAiMonthCostsAsync(baseUrl, apiKey, cancellationToken).ConfigureAwait(false);

            case ProviderBalanceKind.Deepgram:
            {
                // Balances are kept per project; the key's projects together are the company's credit.
                using var projects = await GetAsync($"{baseUrl}/v1/projects", apiKey, cancellationToken, DeepgramAuth)
                    .ConfigureAwait(false);
                var balances = new List<JsonDocument>();
                try
                {
                    foreach (var projectId in ProviderBalanceParser.DeepgramProjectIds(projects.RootElement))
                    {
                        balances.Add(await GetAsync(
                                $"{baseUrl}/v1/projects/{Uri.EscapeDataString(projectId)}/balances",
                                apiKey,
                                cancellationToken,
                                DeepgramAuth)
                            .ConfigureAwait(false));
                    }

                    return ProviderBalanceParser.TryParseDeepgramBalances(balances.Select(b => b.RootElement), out var reading)
                        ? reading
                        : throw new InvalidOperationException("The provider's balance endpoint returned no balance.");
                }
                finally
                {
                    balances.ForEach(b => b.Dispose());
                }
            }

            case ProviderBalanceKind.ElevenLabs:
            {
                using var subscription = await GetAsync(
                        $"{baseUrl}/v1/user/subscription",
                        apiKey,
                        cancellationToken,
                        static (request, key) => request.Headers.Add("xi-api-key", key))
                    .ConfigureAwait(false);
                return ProviderBalanceParser.TryParseElevenLabsSubscription(subscription.RootElement, out var reading)
                    ? reading
                    : throw new InvalidOperationException("The provider's subscription endpoint returned no credit figures.");
            }

            default:
                throw new NotSupportedException($"{provider.DisplayName} has no balance the gateway can read.");
        }
    }

    /// <summary>
    /// OpenAI's spend since the start of the UTC month, summed from the organization's daily cost
    /// buckets. There is no balance to read, so the reading carries what was used and nothing remaining.
    /// </summary>
    private async Task<ProviderBalanceReading> ReadOpenAiMonthCostsAsync(
        string baseUrl,
        string adminKey,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var url = $"{baseUrl}/v1/organization/costs?start_time={monthStart.ToUnixTimeSeconds()}&bucket_width=1d&limit=31";

        decimal used = 0m;
        var sawBuckets = false;
        string? page = null;
        for (var pages = 0; pages < MaxCostPages; pages++)
        {
            using var costs = await GetAsync(
                    page is null ? url : $"{url}&page={Uri.EscapeDataString(page)}",
                    adminKey,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!ProviderBalanceParser.TryParseOpenAiCostsPage(costs.RootElement, out var pageUsed, out page))
            {
                break;
            }

            sawBuckets = true;
            used += pageUsed;
            if (page is null)
            {
                break;
            }
        }

        return sawBuckets
            ? new ProviderBalanceReading(null, null, used, "USD", DateTimeOffset.UtcNow, null)
            : throw new InvalidOperationException("The provider's costs endpoint returned no cost buckets.");
    }

    /// <summary>A month is at most 31 daily buckets; the bound only guards against a cursor that never ends.</summary>
    private const int MaxCostPages = 8;

    private static void BearerAuth(HttpRequestMessage request, string apiKey) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

    private static void DeepgramAuth(HttpRequestMessage request, string apiKey) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Token", apiKey);

    private async Task<JsonDocument> GetAsync(
        string url,
        string apiKey,
        CancellationToken cancellationToken,
        Action<HttpRequestMessage, string>? authorize = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        (authorize ?? BearerAuth)(request, apiKey);

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // The status only: a provider's error body can echo the key it was sent.
            throw new HttpRequestException($"The provider answered HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
