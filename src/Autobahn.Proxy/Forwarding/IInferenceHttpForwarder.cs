using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Application.Errors;
using Autobahn.Application.Forwarding;
using Autobahn.Proxy.Routing;
using Yarp.ReverseProxy.Forwarder;

namespace Autobahn.Proxy.Forwarding;
public interface IInferenceHttpForwarder
{
    /// <summary>
    /// Forwards the current request to <paramref name = "modelUrl"/>.
    /// </summary>
    /// <param name = "timeouts">
    /// Header and stream-idle deadlines. The forwarder owns both, so the caller must pass an
    /// undeadlined <paramref name = "cancellationToken"/> (normally <c>HttpContext.RequestAborted</c>)
    /// and rely on the returned <see cref = "ForwarderError"/> to tell the two apart.
    /// </param>
    /// <param name = "cancellationToken">
    /// Client-disconnect / shutdown token only. It must NOT carry a total-duration deadline —
    /// doing so is what previously truncated healthy long streams.
    /// </param>
    /// <returns>
    /// <see cref = "ForwarderError.None"/> on success;
    /// <see cref = "ForwarderError.Request"/> when the upstream could not be reached or rejected the
    /// request before answering (a backend health signal);
    /// <see cref = "ForwarderError.RequestTimedOut"/> when headers did not arrive in time (a backend
    /// health signal);
    /// <see cref = "ForwarderError.ResponseBodyCanceled"/> when a body stalled past the idle timeout
    /// (inconclusive — abandon the probe);
    /// <see cref = "ForwarderError.ResponseBodyDestination"/> when the upstream answered but its body
    /// then failed mid-transfer — connection reset, premature EOF, malformed framing (a backend
    /// health signal; the client may already have received part of the body);
    /// <see cref = "ForwarderError.RequestCanceled"/> when the client went away.
    /// </returns>
    /// <remarks>
    /// When an error is reported before the response has started, any upstream headers already
    /// copied onto the response have been removed again, so the caller can write a gateway error
    /// over a clean header set.
    /// </remarks>
    Task<ForwarderError> SendAsync(HttpContext context, string modelUrl, string? upstreamBearerToken, StreamingHttpTransformer transformer, bool isStreaming, InferenceForwardTimeouts timeouts, CancellationToken cancellationToken);
}
