using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Autobahn.Api.Contracts;
using Autobahn.Application.Abstractions;
using Autobahn.Application.Diagnostics;
using Autobahn.Domain.Models;

namespace Autobahn.Api.Services;
/// <summary>
/// The upstream request a health check sends for one model type. IsHealthyBody decides whether a
/// 2xx really is a pass — a chat model may legitimately return empty content, but an embeddings
/// response with no vectors is a failure however the upstream framed it. SummarizeSuccess is
/// display-only and may return null.
/// </summary>
public sealed record ModelTestProbe(Uri RequestUri, string EndpointPath, object Payload, Func<string, bool> IsHealthyBody, Func<string, string?> SummarizeSuccess);
