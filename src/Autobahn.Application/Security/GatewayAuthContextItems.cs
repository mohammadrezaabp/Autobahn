namespace Autobahn.Application.Security;
public static class GatewayAuthContextItems
{
    /// <summary>
    /// Set when authentication rejected the request, carrying the error code to report.
    /// </summary>
    /// <remarks>
    /// Distinguishes "this request may be served anonymously" from "a credential the gateway issued
    /// was presented and is no longer usable". Only the former reaches the anonymous paths (public
    /// models, model listing); treating a revoked or expired key as anonymous returned 200 to its
    /// holder, so nothing downstream could tell that the credential had stopped working. A key
    /// matching no stored record is not a rejection at all on those paths — see
    /// <see cref = "ApiKeyValidationFailureExtensions.IsRecognizedCredential"/>.
    /// </remarks>
    public const string AuthFailureCode = "Gateway:AuthFailureCode";
    /// <summary>
    /// Set to <c>true</c> by the security layer when it writes a <c>401</c> for a credential it
    /// could not accept: missing, unknown, expired, revoked, or belonging to a deactivated tenant.
    /// </summary>
    /// <remarks>
    /// The auth-failure rate limiter charges a client address for exactly these outcomes and reads
    /// this item rather than the response status. A status code cannot tell a refused credential
    /// apart from a <c>403</c> the router wrote for an ungranted model or a <c>401</c> copied
    /// verbatim from an upstream provider — and charging those meant a valid key could lock its own
    /// address out, and an expired upstream credential locked out every address at once. Nothing
    /// outside the security layer sets this.
    /// </remarks>
    public const string CredentialRejected = "Gateway:CredentialRejected";
}
