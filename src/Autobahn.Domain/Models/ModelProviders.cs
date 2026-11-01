namespace Autobahn.Domain.Models;
/// <summary>
/// Who serves a model: the company's own hardware, or a provider that bills per request. Access
/// to a paid provider is closed until it is opened for a tenant and for a key
/// (see <c>ModelGrantEvaluator</c>); a self-hosted model needs only its place on the key's list.
/// </summary>
public static class ModelProviders
{
    public const string SelfHosted = "self-hosted";
    public const string OpenRouter = "openrouter";
    public const string OpenAi = "openai";
    public const string DeepSeek = "deepseek";
    public const string Deepgram = "deepgram";
    public const string ElevenLabs = "elevenlabs";
    private static readonly ModelProviderDefinition[] PaidDefinitions = [new(OpenRouter, "OpenRouter", ["openrouter.ai"], "https://openrouter.ai/api", "OPENROUTER_API_KEY", "https://openrouter.ai/api/v1/models", ReportsCost: true, ProviderBalanceKind.OpenRouter), new(OpenAi, "OpenAI", ["api.openai.com"], "https://api.openai.com", "OPENAI_API_KEY", "https://api.openai.com/v1/models", ReportsCost: false, ProviderBalanceKind.OpenAi)
    {
        StreamUsageOnRequest = true,
        PinnedServiceTier = "default",
        BalanceEnvVar = "OPENAI_ADMIN_KEY",
    }, new(DeepSeek, "DeepSeek", ["api.deepseek.com"], "https://api.deepseek.com", "DEEPSEEK_API_KEY", "https://api.deepseek.com/v1/models", ReportsCost: false, ProviderBalanceKind.DeepSeek), new(Deepgram, "Deepgram", ["api.deepgram.com"], "https://api.deepgram.com", "DEEPGRAM_API_KEY", CatalogUrl: null, ReportsCost: false, ProviderBalanceKind.Deepgram, Forwardable: false), new(ElevenLabs, "ElevenLabs", ["api.elevenlabs.io"], "https://api.elevenlabs.io", "ELEVENLABS_API_KEY", CatalogUrl: null, ReportsCost: false, ProviderBalanceKind.ElevenLabs, Forwardable: false), ];
    /// <summary>Every recognised provider, self-hosted first.</summary>
    public static readonly IReadOnlyList<string> All = [SelfHosted, ..PaidDefinitions.Select(d => d.Id)];
    /// <summary>The providers that bill per request, in the order the console lists them.</summary>
    public static IReadOnlyList<ModelProviderDefinition> Paid => PaidDefinitions;

    public static bool IsPaid(string provider) => !string.Equals(provider, SelfHosted, StringComparison.OrdinalIgnoreCase);
    public static bool TryGetPaid(string? provider, out ModelProviderDefinition definition)
    {
        definition = PaidDefinitions.FirstOrDefault(d => string.Equals(d.Id, provider?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return definition is not null;
    }

    public static string DisplayName(string provider) => TryGetPaid(provider, out var definition) ? definition.DisplayName : "Self-hosted";
    /// <summary>
    /// Folds <paramref name = "provider"/> onto a recognised id. Blank is valid and comes back as
    /// null: the provider is then read from the model's backend addresses.
    /// </summary>
    public static bool TryNormalize(string? provider, out string? normalized, out string? error)
    {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(provider))
        {
            return true;
        }

        var trimmed = provider.Trim();
        normalized = All.FirstOrDefault(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
        if (normalized is not null)
        {
            return true;
        }

        error = $"provider '{trimmed}' is not recognised. Expected one of: {string.Join(", ", All)}.";
        return false;
    }

    /// <summary>
    /// The provider to treat <paramref name = "model"/> as: the one an operator set, otherwise the
    /// one its backend addresses say.
    /// </summary>
    /// <remarks>
    /// <para>Read from the primary backends, which share the model's traffic: one paid backend
    /// among them is enough to make the model a paid one, because some of every caller's requests
    /// go to it. Reading it as self-hosted would open that spend to every key on the model's
    /// list.</para>
    ///
    /// <para>A standby backend does not count. It takes a request only when no primary can, and
    /// only for a key opened for the standby's provider (see <see cref = "ResolveBackend"/>), so a
    /// self-hosted model with a paid standby is still a self-hosted model to every other key. A
    /// model with nothing but standbys is read from those.</para>
    /// </remarks>
    public static string Resolve(ModelConfig model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (TryNormalize(model.Provider, out var set, out _) && set is not null)
        {
            return set;
        }

        var backends = model.GetBackends();
        var primaries = backends.Where(b => !b.IsStandby()).ToList();
        return (primaries.Count > 0 ? primaries : backends).Select(b => FromUrl(b.Url)).FirstOrDefault(IsPaid) ?? SelfHosted;
    }

    /// <summary>
    /// The provider one backend's requests are billed by. A primary backend is billed as the
    /// model is, so an operator's correction on the model holds; a standby is read from its own
    /// address, since it is by definition somewhere else.
    /// </summary>
    public static string ResolveBackend(ModelConfig model, ModelBackendConfig backend)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(backend);
        return backend.IsStandby() ? FromUrl(backend.Url) : Resolve(model);
    }

    /// <summary>The provider a backend address belongs to; self-hosted when it is none of the paid ones.</summary>
    public static string FromUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return SelfHosted;
        }

        foreach (var definition in PaidDefinitions)
        {
            foreach (var host in definition.Hosts)
            {
                if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
                {
                    return definition.Id;
                }
            }
        }

        return SelfHosted;
    }
}
