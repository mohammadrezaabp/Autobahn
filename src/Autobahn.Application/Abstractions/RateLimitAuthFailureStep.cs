using Autobahn.Domain.RateLimiting;

namespace Autobahn.Application.Abstractions;
/// <summary>The three things the failed-credential limiter does.</summary>
public enum RateLimitAuthFailureStep
{
    /// <summary>A credentialed request was checked against the address block's budget and let on.</summary>
    Checked = 0,
    /// <summary>The budget was spent and the credential could not be proven: answered 429.</summary>
    Refused = 1,
    /// <summary>The credential was rejected downstream, so one token was debited.</summary>
    Charged = 2,
}
