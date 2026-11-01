using System.Text.Json;
using Autobahn.Application.Abstractions;

namespace Autobahn.Api.Contracts;
/// <summary>One save, or one refused attempt.</summary>
public sealed class AdminRateLimitHistoryEntryDto
{
    /// <summary>
    /// Most rule changes carried per entry, at both ends: the writer records no more than this and
    /// the reader returns no more than this. The rest are counted, not listed.
    /// </summary>
    public const int MaxChanges = 200;
    public DateTimeOffset TimestampUtc { get; set; }
    /// <summary><c>applied</c> or <c>refused</c>.</summary>
    public string Outcome { get; set; } = "applied";
    /// <summary>The tenant the acting admin key belongs to. An id, never a secret.</summary>
    public string? ActorTenantId { get; set; }
    /// <summary>The admin key that acted. An id, never the key.</summary>
    public string? ActorApiKeyId { get; set; }
    /// <summary>Refused only: the HTTP status the attempt got (400 invalid, 409 conflict, 503 no store, 500).</summary>
    public int? StatusCode { get; set; }
    /// <summary>Refused only: the message the caller was given.</summary>
    public string? Message { get; set; }
    /// <summary>The configuration version the save produced. Null on a refusal and on entries written before versions were recorded.</summary>
    public long? Version { get; set; }
    /// <summary>The version the caller's change was based on (its <c>If-Match</c>); null when it sent none.</summary>
    public long? BasedOnVersion { get; set; }
    /// <summary>Applied only: the master switch as saved.</summary>
    public bool? Enabled { get; set; }
    /// <summary>Applied only: the adaptive switch as saved.</summary>
    public bool? AdaptiveEnabled { get; set; }
    /// <summary>Applied only: rules in the saved set. Null when the caller did not send rules.</summary>
    public int? RuleCount { get; set; }
    /// <summary>
    /// Rule changes, at most <see cref = "MaxChanges"/>. Null when the entry did not record any —
    /// the caller sent no rule list — which is different from an empty list (rules sent, none changed).
    /// </summary>
    public IReadOnlyList<AdminRateLimitRuleChangeDto>? Changes { get; set; }
    /// <summary>How many rule changes the entry recorded, including any not listed.</summary>
    public int ChangeCount { get; set; }
    public bool ChangesTruncated { get; set; }

    public static AdminRateLimitHistoryEntryDto FromAudit(AuditLogEntryView entry)
    {
        var dto = new AdminRateLimitHistoryEntryDto
        {
            TimestampUtc = entry.TimestampUtc,
            Outcome = entry.Action.EndsWith("_refused", StringComparison.Ordinal) ? "refused" : "applied",
            ActorTenantId = entry.TenantId,
            ActorApiKeyId = entry.ApiKeyId,
        };
        if (string.IsNullOrEmpty(entry.Details))
        {
            return dto;
        }

        try
        {
            using var document = JsonDocument.Parse(entry.Details);
            var details = document.RootElement;
            if (details.ValueKind != JsonValueKind.Object)
            {
                return dto;
            }

            // Field by field, never the raw details: what reaches the console is what is listed
            // here, so a field added to the audit record later is not published by accident.
            dto.StatusCode = Int(details, "statusCode");
            dto.Message = dto.Outcome == "refused" ? Text(details, "message") : null;
            dto.Version = Long(details, "version");
            dto.BasedOnVersion = Long(details, "basedOnVersion");
            dto.Enabled = Bool(details, "enabled");
            dto.AdaptiveEnabled = Bool(details, "adaptiveEnabled");
            dto.RuleCount = Int(details, "ruleCount");
            ReadChanges(details, dto);
        }
        catch (JsonException)
        {
        // A malformed details blob costs the detail, not the entry.
        }

        return dto;
    }

    private static void ReadChanges(JsonElement details, AdminRateLimitHistoryEntryDto dto)
    {
        var structured = details.TryGetProperty("changedRules", out var rules) && rules.ValueKind == JsonValueKind.Array;
        var legacy = details.TryGetProperty("changes", out var lines) && lines.ValueKind == JsonValueKind.Array;
        if (!structured && !legacy)
        {
            return;
        }

        var changes = new List<AdminRateLimitRuleChangeDto>();
        var total = 0;
        if (structured)
        {
            foreach (var item in rules.EnumerateArray())
            {
                total++;
                if (changes.Count < MaxChanges && item.ValueKind == JsonValueKind.Object)
                {
                    changes.Add(new AdminRateLimitRuleChangeDto { Kind = Text(item, "kind") ?? "changed", RuleId = Text(item, "ruleId"), Before = Text(item, "before"), After = Text(item, "after"), });
                }
            }
        }
        else
        {
            // Entries written before changes were recorded as records. The sentence is passed
            // through whole and no rule id is claimed for it: taking it apart would be guessing.
            foreach (var line in lines.EnumerateArray())
            {
                total++;
                if (changes.Count < MaxChanges && line.ValueKind == JsonValueKind.String)
                {
                    var text = line.GetString() ?? string.Empty;
                    changes.Add(new AdminRateLimitRuleChangeDto { Kind = text.StartsWith("added ", StringComparison.Ordinal) ? "added" : text.StartsWith("removed ", StringComparison.Ordinal) ? "removed" : "changed", Summary = text, });
                }
            }
        }

        // The writer caps what it records and stores the true total beside it, so an entry that
        // described thousands of changes still reports how many there were.
        dto.Changes = changes;
        dto.ChangeCount = Math.Max(Int(details, "changeCount") ?? 0, total);
        dto.ChangesTruncated = dto.ChangeCount > changes.Count;
    }

    private static string? Text(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static int? Int(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;
    private static long? Long(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : null;
    private static bool? Bool(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;
}
