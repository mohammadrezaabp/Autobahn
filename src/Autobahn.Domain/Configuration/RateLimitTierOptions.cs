using System.Text.Json.Serialization;

namespace Autobahn.Domain.Configuration;
public sealed class RateLimitTierOptions
{
    public int Rpm { get; set; } = 600;
    public int Burst { get; set; } = 100;
    /// <summary>Concurrent streaming responses allowed per partition. Zero means unlimited.</summary>
    public int MaxConcurrentStreams { get; set; } = 50;
    /// <summary>Tokens per minute allowed per partition, prompt and completion together. Zero means unlimited.</summary>
    public int Tpm { get; set; }

    /// <summary>A tier that enforces nothing — the shape an unset optional scope takes.</summary>
    public static RateLimitTierOptions Unset() => new()
    {
        Rpm = 0,
        Burst = 0,
        MaxConcurrentStreams = 0
    };
    /// <summary>
    /// No control is on. Not serialised: this type is the admin API's tier shape, and a derived
    /// flag on the wire is a field clients would start reading and the gateway would have to keep.
    /// </summary>
    [JsonIgnore]
    public bool EnforcesNothing => Rpm <= 0 && MaxConcurrentStreams <= 0 && Tpm <= 0;
}
