namespace Autobahn.Domain.Models;

public static class UpstreamSecretRefs
{
    public const string FileModelPrefix = "file:model:";

    public static string ForModel(string modelId) =>
        FileModelPrefix + modelId.Trim();

    /// <summary>
    /// The key a backend's credential is stored under. The default backend's is the model id, so
    /// the credential of a model written in the one-url shape stays where it always was.
    /// </summary>
    public static string StoreKeyForBackend(string modelId, string? backendName) =>
        ModelBackends.IsDefault(backendName) || string.IsNullOrWhiteSpace(backendName)
            ? modelId.Trim()
            : $"{modelId.Trim()}:backend:{backendName.Trim()}";

    public static string ForBackend(string modelId, string? backendName) =>
        FileModelPrefix + StoreKeyForBackend(modelId, backendName);

    public static bool IsValidForBackend(string? secretRef, string modelId, string? backendName) =>
        TryParseModelId(secretRef, out var parsed) &&
        string.Equals(parsed, StoreKeyForBackend(modelId, backendName), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The key the referenced credential is stored under: the model id, or the model id and a
    /// backend's name (<see cref="StoreKeyForBackend"/>).
    /// </summary>
    public static bool TryParseModelId(string? secretRef, out string modelId)
    {
        modelId = string.Empty;
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            return false;
        }

        if (!secretRef.StartsWith(FileModelPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var id = secretRef[FileModelPrefix.Length..].Trim();
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        modelId = id;
        return true;
    }

    public static bool IsValidForModel(string? secretRef, string modelId)
    {
        if (!TryParseModelId(secretRef, out var parsed))
        {
            return false;
        }

        return string.Equals(parsed, modelId.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
