namespace Autobahn.Infrastructure.Fleet;

public sealed class GitOpsOptions
{
    public string Url { get; set; } = "";
    public string Project { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string Token { get; set; } = "";
}
