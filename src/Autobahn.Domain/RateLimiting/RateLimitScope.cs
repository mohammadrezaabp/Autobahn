namespace Autobahn.Domain.RateLimiting;
/// <summary>
/// The dimension a rate-limit rule counts against. Every scope that applies to a request is
/// evaluated; the scopes do not override one another, so a request is admitted only when all of
/// them admit it.
/// </summary>
/// <remarks>
/// <para>The declaration order is the evaluation order, and it is deliberate in two ways.</para>
///
/// <para>First, it is the fixed order every caller acquires in. Multi-scope admission takes a token
/// from each applicable bucket and refunds them all if a later scope refuses, so a stable order is
/// what makes the refund set well defined regardless of which scopes a given request happens to
/// have.</para>
///
/// <para>Second, the model-independent scopes come first because the model is not free to learn.
/// Reading it means buffering and parsing the request body, and a caller that is already over its
/// tenant or key budget must be refused before the gateway pays for that. Everything below
/// <see cref = "Model"/> is therefore evaluated in a second stage, after the parse, and only for
/// requests the first stage admitted.</para>
/// </remarks>
public enum RateLimitScope
{
    /// <summary>Every inference request through the gateway, regardless of caller or model.</summary>
    Global = 0,
    /// <summary>
    /// One tenant, or — for unauthenticated traffic to a public model — one client address block.
    /// This is the scope the gateway enforced before scoped rules existed.
    /// </summary>
    Tenant = 1,
    /// <summary>One API key. Bounds a single credential inside its tenant's larger allowance.</summary>
    ApiKey = 2,
    /// <summary>One model, summed across every caller. The model's own capacity, shared fairly.</summary>
    Model = 3,
    /// <summary>One tenant's use of one model.</summary>
    TenantModel = 4,
    /// <summary>One API key's use of one model — the narrowest scope there is.</summary>
    ApiKeyModel = 5,
    /// <summary>
    /// Requests refused by authentication, counted per client address. Evaluated by its own
    /// middleware ahead of the security layer, never as part of a request's rule set.
    /// </summary>
    AuthFailure = 6,
}
