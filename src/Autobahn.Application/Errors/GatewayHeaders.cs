namespace Autobahn.Application.Errors;

public static class GatewayHeaders
{
    public const string RequestId = "X-Request-Id";
    public const string ErrorCode = "X-Autobahn-Error-Code";
    public const string RetryAfter = "Retry-After";

    public const string QuotaWarning = "X-Autobahn-Quota-Warning";

    /// <summary>
    /// Who served the request (<c>self-hosted</c>, <c>openrouter</c>, …): the provider of the
    /// backend it was forwarded to.
    /// </summary>
    public const string Provider = "X-Autobahn-Provider";

    /// <summary>
    /// Present, as <c>true</c>, when a standby backend carried the request because none of the
    /// model's primary backends could.
    /// </summary>
    public const string Standby = "X-Autobahn-Standby";

    /// <summary>The partition's request budget (bucket capacity: <c>rpm + burst</c>).</summary>
    /// <remarks>
    /// Vendor-prefixed rather than the conventional <c>X-RateLimit-*</c> on purpose: the response
    /// headers the upstream provider returns are copied onto the response after this middleware has
    /// run, so an unprefixed name would be silently overwritten by the provider's own budget — a
    /// number about a different limit entirely. The prefix also matches the rest of the gateway's
    /// headers.
    /// </remarks>
    public const string RateLimitLimit = "X-Autobahn-RateLimit-Limit";

    /// <summary>Whole requests left in the partition's budget after the current request.</summary>
    public const string RateLimitRemaining = "X-Autobahn-RateLimit-Remaining";

    /// <summary>Seconds until the partition is back at its full budget.</summary>
    public const string RateLimitReset = "X-Autobahn-RateLimit-Reset";

    /// <summary>
    /// Which scope the reported budget belongs to — <c>global</c>, <c>tenant</c>, <c>api_key</c>,
    /// <c>model</c>, <c>tenant_model</c> or <c>api_key_model</c>.
    /// </summary>
    /// <remarks>
    /// Several limits apply to one request, so a bare remaining-count is ambiguous: a client that
    /// sees 4 left cannot tell whether it is its own key, its whole organisation, or the model it
    /// chose that is nearly exhausted, and those call for three different responses. On a rejection
    /// this names the scope that refused; on a success, the one closest to refusing.
    /// </remarks>
    public const string RateLimitScope = "X-Autobahn-RateLimit-Scope";

    /// <summary>
    /// Present, as <c>tokens</c>, only on a refusal by a tokens-per-minute limit: the limit and
    /// remaining headers then count tokens, not requests.
    /// </summary>
    /// <remarks>
    /// Without it a client reads a limit of 200000 as requests. A token refusal also calls for a
    /// different response than a request-rate one: waiting helps either way, but so does sending a
    /// smaller prompt or a lower <c>max_tokens</c>.
    /// </remarks>
    public const string RateLimitUnit = "X-Autobahn-RateLimit-Unit";

    /// <summary>
    /// Present only while load-aware adaptation is holding the reported scope below its configured
    /// rate, as <c>&lt;effective&gt;/&lt;configured&gt;</c> requests per minute.
    /// </summary>
    /// <remarks>
    /// Absent means "you are being enforced exactly as configured", which is the answer to the first
    /// question anyone asks when a limit behaves unexpectedly. Emitting it unconditionally would make
    /// the interesting case invisible among the ordinary ones.
    /// </remarks>
    public const string RateLimitAdaptive = "X-Autobahn-RateLimit-Adaptive";

    /// <summary>
    /// The IETF <c>RateLimit-*</c> names, written <em>only</em> on a response the gateway refuses
    /// itself.
    /// </summary>
    /// <remarks>
    /// <para>The vendor prefix above exists because an upstream provider's own budget headers are
    /// copied onto the response after the limiter has run, so an unprefixed name would be silently
    /// overwritten by a number about a different limit entirely. That collision cannot happen on a
    /// refusal: a <c>429</c> the gateway writes never reaches an upstream, so there is nothing to be
    /// overwritten by — and on exactly the response a client most needs to read, the standard names
    /// carried nothing.</para>
    ///
    /// <para>Both spellings are sent. Generic clients and SDK middleware read these; anything written
    /// against the gateway keeps reading the prefixed ones, which are still the only ones present on
    /// an admitted response.</para>
    /// </remarks>
    public const string StandardRateLimitLimit = "RateLimit-Limit";

    /// <inheritdoc cref="StandardRateLimitLimit"/>
    public const string StandardRateLimitRemaining = "RateLimit-Remaining";

    /// <inheritdoc cref="StandardRateLimitLimit"/>
    public const string StandardRateLimitReset = "RateLimit-Reset";
}
