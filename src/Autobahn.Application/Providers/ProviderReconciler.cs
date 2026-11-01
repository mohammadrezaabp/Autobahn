namespace Autobahn.Application.Providers;
public static class ProviderReconciler
{
    /// <summary>Reads the window's balance readings and the gateway's charges between them, and reconciles them.</summary>
    public static async Task<ProviderReconciliationReport> ReconcileAsync(Autobahn.Application.Abstractions.IProviderBalanceHistoryRepository history, string provider, DateTimeOffset windowFrom, DateTimeOffset windowTo, ProviderDriftThreshold threshold, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(history);
        var readings = await history.ListAsync(provider, windowFrom, windowTo, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<ProviderCharge> charges = readings.Count < 2 ? [] : await history.ListChargesAsync(provider, readings[0].ReadAt, readings[^1].ReadAt, cancellationToken).ConfigureAwait(false);
        return Reconcile(provider, windowFrom, windowTo, readings, charges, threshold);
    }

    /// <summary>
    /// Walks consecutive balance readings. Between two of them, the provider spent what the
    /// balance fell by, and the gateway charged what its requests recorded in between cost. A
    /// stretch in which the balance rose held a top-up, which hides whatever was spent alongside
    /// it, so that stretch counts on neither side.
    /// </summary>
    /// <param name = "readings">Every reading in the window, plus the last one before it if any.</param>
    /// <param name = "charges">The gateway's charges at the provider from the first reading on.</param>
    public static ProviderReconciliationReport Reconcile(string provider, DateTimeOffset windowFrom, DateTimeOffset windowTo, IReadOnlyList<ProviderBalanceSnapshot> readings, IReadOnlyList<ProviderCharge> charges, ProviderDriftThreshold threshold)
    {
        ArgumentNullException.ThrowIfNull(readings);
        ArgumentNullException.ThrowIfNull(charges);
        ArgumentNullException.ThrowIfNull(threshold);
        var ordered = readings.OrderBy(r => r.ReadAt).ToList();
        var currency = ordered.Count > 0 ? ordered[^1].Currency : "USD";
        if (ordered.Count < 2)
        {
            return Empty(provider, currency, windowFrom, windowTo, ordered.Count, threshold, "At least two balance readings are needed; they are taken every few minutes.");
        }

        if (ordered.Any(r => !string.Equals(r.Currency, "USD", StringComparison.OrdinalIgnoreCase)))
        {
            return Empty(provider, currency, windowFrom, windowTo, ordered.Count, threshold, "The provider reports its balance in a currency other than USD, which the gateway's charges are not in.");
        }

        var times = charges.OrderBy(c => c.RecordedAt).ToList();
        var next = 0;
        decimal providerTotal = 0m, gatewayTotal = 0m;
        var topUps = 0;
        var days = new SortedDictionary<DateOnly, (decimal Provider, decimal Gateway)>();
        for (var i = 1; i < ordered.Count; i++)
        {
            var start = ordered[i - 1];
            var end = ordered[i];
            // Charges in (start, end]: a request recorded at the instant of a reading counts before it.
            while (next < times.Count && times[next].RecordedAt <= start.ReadAt)
            {
                next++;
            }

            decimal charged = 0m;
            while (next < times.Count && times[next].RecordedAt <= end.ReadAt)
            {
                charged += times[next].Cost;
                next++;
            }

            var fell = start.Remaining - end.Remaining;
            if (fell < 0m)
            {
                topUps++;
                continue;
            }

            providerTotal += fell;
            gatewayTotal += charged;
            var day = DateOnly.FromDateTime(end.ReadAt.UtcDateTime);
            var sums = days.TryGetValue(day, out var existing) ? existing : (0m, 0m);
            days[day] = (sums.Item1 + fell, sums.Item2 + charged);
        }

        var difference = providerTotal - gatewayTotal;
        var larger = Math.Max(providerTotal, gatewayTotal);
        decimal? percent = larger == 0m ? null : decimal.Round(Math.Abs(difference) / larger * 100m, 2);
        var drifting = Math.Abs(difference) >= threshold.MinimumUsd && percent is { } p && p >= threshold.Percent;
        return new ProviderReconciliationReport
        {
            Provider = provider,
            Currency = "USD",
            WindowFrom = windowFrom,
            WindowTo = windowTo,
            CoveredFrom = ordered[0].ReadAt,
            CoveredTo = ordered[^1].ReadAt,
            Readings = ordered.Count,
            TopUps = topUps,
            ProviderSpendUsd = providerTotal,
            GatewaySpendUsd = gatewayTotal,
            DifferenceUsd = difference,
            DifferencePercent = percent,
            Drifting = drifting,
            Threshold = threshold,
            Daily = days.Select(d => new ProviderReconciliationDay(d.Key, d.Value.Provider, d.Value.Gateway)).ToList(),
        };
    }

    private static ProviderReconciliationReport Empty(string provider, string currency, DateTimeOffset windowFrom, DateTimeOffset windowTo, int readings, ProviderDriftThreshold threshold, string reason) => new()
    {
        Provider = provider,
        Currency = currency,
        WindowFrom = windowFrom,
        WindowTo = windowTo,
        Readings = readings,
        Threshold = threshold,
        Unavailable = reason,
        Daily = [],
    };
}
