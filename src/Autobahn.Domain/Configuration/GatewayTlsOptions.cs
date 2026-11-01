namespace Autobahn.Domain.Configuration;

public sealed class GatewayTlsOptions
{
    public const string SectionName = "Tls";

    public bool ValidateUpstreamCertificates { get; set; } = true;
}
