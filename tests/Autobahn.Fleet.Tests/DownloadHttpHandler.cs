using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Autobahn.Fleet.Tests;

internal sealed class DownloadHttpHandler(string filePath = "weights.bin") : HttpMessageHandler
{
    public static readonly byte[] Payload = Encoding.UTF8.GetBytes("hello");
    public const string Revision = "abcdef1234567890abcdef1234567890abcdef12";
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri!.AbsolutePath.StartsWith("/api/models/", StringComparison.Ordinal))
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JsonObject
            { ["sha"] = Revision, ["siblings"] = new JsonArray(new JsonObject { ["rfilename"] = filePath, ["size"] = Payload.Length,
                ["lfs"] = new JsonObject { ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Payload)) } }) }.ToJsonString(), Encoding.UTF8, "application/json") });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) });
    }
}
