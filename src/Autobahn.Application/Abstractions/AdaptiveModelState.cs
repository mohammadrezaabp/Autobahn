using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <param name = "ModelId">The model.</param>
/// <param name = "Factor">The multiplier currently applied to its per-model rules.</param>
/// <param name = "Saturation">
/// How full the model's forwarding capacity is, in <c>[0, 1+]</c> — the larger of its in-flight and
/// its queue occupancy. This is the input the factor is derived from.
/// </param>
/// <param name = "Reason">Why the factor last moved, in words an operator can act on.</param>
/// <param name = "UpdatedUtc">When the factor last changed.</param>
public sealed record AdaptiveModelState(string ModelId, double Factor, double Saturation, string Reason, DateTimeOffset UpdatedUtc);
