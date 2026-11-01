using System.Collections.Concurrent;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Providers;
/// <summary>The last reading of a paid provider's credit.</summary>
/// <param name = "Remaining">Null when the provider reported usage but no limit to subtract it from.</param>
/// <param name = "ReadAt">When figures were last read successfully; null when they never were.</param>
/// <param name = "Error">Why the most recent attempt failed; null when it succeeded.</param>
public sealed record ProviderBalanceReading(decimal? Remaining, decimal? Limit, decimal? Used, string Currency, DateTimeOffset? ReadAt, string? Error);
