using System.Net.Http.Json;

namespace Autobahn.Integration.Tests.Support;

internal static class ModelGrantTestHelpers
{
    public static async Task GrantApiKeyModelsAsync(
        HttpClient adminClient,
        Guid apiKeyId,
        params string[] modelIds)
    {
        var response = await adminClient.PutAsJsonAsync(
            $"/admin/api/keys/{apiKeyId}/model-grants",
            new { modelIds });
        response.EnsureSuccessStatusCode();
    }
}
