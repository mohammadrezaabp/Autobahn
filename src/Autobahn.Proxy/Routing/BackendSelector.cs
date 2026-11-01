using Autobahn.Domain.Models;

namespace Autobahn.Proxy.Routing;

/// <summary>
/// Orders a model's available backends for one request: first by size, then by load.
/// </summary>
/// <remarks>
/// <para><b>By size.</b> A backend whose context is smaller than the request is left out, and
/// among the rest the smallest context comes first. That is what keeps ordinary requests off a
/// backend built for long ones — fewest-in-flight alone would send them there whenever it is idle.
/// A backend with no known limit (0) holds anything and ranks as the largest.</para>
///
/// <para><b>By load.</b> Within one size, the backend with the fewest requests in flight comes
/// first; equals are shuffled so they share the traffic rather than the first always winning.</para>
///
/// <para>The larger sizes follow in the order, so a caller that finds the first choice unavailable
/// (its circuit refuses) moves on to them. A size that is only busy is never passed over.</para>
///
/// <para>The estimate decides between backends and never refuses a request. When no backend is
/// large enough the largest is offered and the upstream gives the answer: bytes divided by a
/// constant is good enough to pick a tier and not good enough to turn a caller away.</para>
/// </remarks>
public static class BackendSelector
{

    public static IReadOnlyList<ModelBackendConfig> Order(
        IReadOnlyList<BackendCandidate> available,
        long estimatedTokens,
        Random? random = null)
    {
        ArgumentNullException.ThrowIfNull(available);

        if (available.Count == 0)
        {
            return [];
        }

        if (available.Count == 1)
        {
            return [available[0].Backend];
        }

        random ??= Random.Shared;
        var ranked = available
            .Select(c => (c.Backend, c.InFlight, Size: SizeOf(c.Backend), Tie: random.Next()))
            .ToList();

        var fits = ranked.Where(c => c.Size >= estimatedTokens).ToList();
        if (fits.Count == 0)
        {
            return ranked
                .OrderByDescending(c => c.Size)
                .ThenBy(c => c.InFlight)
                .ThenBy(c => c.Tie)
                .Select(c => c.Backend)
                .ToList();
        }

        return fits
            .OrderBy(c => c.Size)
            .ThenBy(c => c.InFlight)
            .ThenBy(c => c.Tie)
            .Select(c => c.Backend)
            .ToList();
    }

    private static long SizeOf(ModelBackendConfig backend) =>
        backend.MaxContextLength > 0 ? backend.MaxContextLength : long.MaxValue;
}
