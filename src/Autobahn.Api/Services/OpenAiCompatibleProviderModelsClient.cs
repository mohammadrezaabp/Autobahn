using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Autobahn.Api.Services;
public sealed class OpenAiCompatibleProviderModelsClient(HttpClient http)
{
    public async Task<IReadOnlyList<ProviderModelListItem>> ListModelsAsync(Uri modelsListUrl, string bearerToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, modelsListUrl);
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderModelsUpstreamException(response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return[];
        }

        var results = new List<ProviderModelListItem>();
        foreach (var item in data.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var name = item.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String ? nameProp.GetString() : null;
            int? contextLength = null;
            if (item.TryGetProperty("context_length", out var ctxProp) && ctxProp.ValueKind == JsonValueKind.Number && ctxProp.TryGetInt32(out var ctx))
            {
                contextLength = ctx;
            }

            // OpenRouter publishes its prices with each model, in USD per token and as strings.
            // A provider that publishes none leaves these null and the price is typed in.
            decimal? input = null, output = null, cached = null, perRequest = null;
            if (item.TryGetProperty("pricing", out var pricing) && pricing.ValueKind == JsonValueKind.Object)
            {
                input = PerMillion(pricing, "prompt");
                output = PerMillion(pricing, "completion");
                cached = PerMillion(pricing, "input_cache_read");
                perRequest = Amount(pricing, "request");
            }

            results.Add(new ProviderModelListItem(id!, name, contextLength) { InputPricePerMillionTokens = input, OutputPricePerMillionTokens = output, CachedInputPricePerMillionTokens = cached, PricePerRequest = perRequest, });
        }

        return results;
    }

    private static decimal? PerMillion(JsonElement pricing, string name) => Amount(pricing, name)is { } perToken ? decimal.Round(perToken * 1_000_000m, 6) : null;
    /// <summary>A price as a number or a numeric string. Negative means "varies"; it is not a price.</summary>
    private static decimal? Amount(JsonElement pricing, string name)
    {
        if (!pricing.TryGetProperty(name, out var element))
        {
            return null;
        }

        decimal value;
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            value = number;
        }
        else if (element.ValueKind != JsonValueKind.String || !decimal.TryParse(element.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
        {
            return null;
        }

        return value < 0m ? null : value;
    }
}
