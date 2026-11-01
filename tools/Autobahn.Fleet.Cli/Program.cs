using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("Autobahn: init [--directory .] | plan|apply|import --url https://gateway.example.com [--commit <planned-commit>] [--node <id>] [--key-env AUTOBAHN_OPERATOR_KEY]");
    return 0;
}
string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
if (args[0] == "init")
{
    var directory = Path.GetFullPath(Option("--directory") ?? ".");
    if (!File.Exists(Path.Combine(directory, "Autobahn.sln"))) { Console.Error.WriteLine("Initialize from the Autobahn repository root, or pass --directory."); return 2; }
    var envPath = Path.Combine(directory, ".env");
    var settings = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var line in File.Exists(envPath) ? File.ReadAllLines(envPath) : [])
    {
        if (line.StartsWith('#') || !line.Contains('=')) continue;
        var separator = line.IndexOf('='); settings[line[..separator]] = line[(separator + 1)..];
    }
    foreach (var name in new[] { "AUTOBAHN_ADMIN_KEY", "AUTOBAHN_KEY_PEPPER", "AUTOBAHN_METRICS_TOKEN", "AUTOBAHN_GRAFANA_PASSWORD" })
    {
        if (!settings.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            settings[name] = (name == "AUTOBAHN_ADMIN_KEY" ? "sk-autobahn-" : "") + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        else if (value.Length < 24) { Console.Error.WriteLine($"{name} is too short. Supply a strong value or leave it empty for initial generation."); return 2; }
    }
    settings.TryAdd("COMPOSE_PROFILES", "");
    File.WriteAllLines(envPath, settings.Select(x => x.Key + "=" + x.Value));
    var secretDirectory = Path.Combine(directory, ".secrets"); Directory.CreateDirectory(secretDirectory);
    var metricsPath = Path.Combine(secretDirectory, "metrics-token"); File.WriteAllText(metricsPath, settings["AUTOBAHN_METRICS_TOKEN"]);
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(envPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(metricsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.SetUnixFileMode(secretDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    Console.WriteLine("Autobahn credentials are saved in .env. Existing credentials were preserved. Use AUTOBAHN_ADMIN_KEY to sign in.");
    return 0;
}
var url = Option("--url");
if (!Uri.TryCreate(url, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https")) { Console.Error.WriteLine("Supply a gateway URL."); return 2; }
var key = Environment.GetEnvironmentVariable(Option("--key-env") ?? "AUTOBAHN_OPERATOR_KEY");
if (string.IsNullOrWhiteSpace(key)) { Console.Error.WriteLine("Set the operator key environment variable before using the fleet command."); return 2; }
using var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = endpoint, Timeout = TimeSpan.FromMinutes(65) };
client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
client.DefaultRequestHeaders.Add("X-AutobahnFleet-Request", "1");
HttpResponseMessage response;
switch (args[0])
{
    case "plan": response = await client.PostAsJsonAsync("/admin/fleet/api/inventory/plan", new { }); break;
    case "apply":
        var commit = Option("--commit"); if (string.IsNullOrWhiteSpace(commit)) { Console.Error.WriteLine("Apply requires the exact commit returned by plan."); return 2; }
        response = await client.PostAsJsonAsync("/admin/fleet/api/inventory/apply", new { commit }); break;
    case "import":
        var node = Option("--node"); if (string.IsNullOrWhiteSpace(node)) { Console.Error.WriteLine("Import requires --node."); return 2; }
        response = await client.GetAsync("/admin/fleet/api/nodes/" + Uri.EscapeDataString(node) + "/import"); break;
    default: Console.Error.WriteLine("Use plan, apply or import."); return 2;
}
using (response)
{
    var body = await response.Content.ReadAsStringAsync();
    try { Console.WriteLine(JsonNode.Parse(body)?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? body); }
    catch (System.Text.Json.JsonException) { Console.WriteLine($"The gateway returned HTTP {(int)response.StatusCode}."); }
    return response.IsSuccessStatusCode ? 0 : 1;
}
