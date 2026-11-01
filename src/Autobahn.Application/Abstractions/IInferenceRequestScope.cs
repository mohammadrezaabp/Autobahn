namespace Autobahn.Application.Abstractions;

/// <summary>
/// Scoped inference request metrics hook. Call <see cref="SetOutcome"/> before dispose when the result is known.
/// </summary>
public interface IInferenceRequestScope : IDisposable
{
    void SetOutcome(bool success, string? errorCode = null);

    /// <summary>
    /// The client hung up before the response finished. Counted as a request and as a disconnect,
    /// never as an error: the gateway and the backend both did their jobs.
    /// </summary>
    void SetClientCanceled() => SetOutcome(false, "client_canceled");

    /// <summary>
    /// Names the provider of the backend the request was forwarded to, for the request, error and
    /// duration series. Unset reads as self-hosted.
    /// </summary>
    void SetProvider(string provider)
    {
    }
}
