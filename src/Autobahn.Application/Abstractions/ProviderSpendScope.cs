namespace Autobahn.Application.Abstractions;
/// <summary>Which monthly cap refused a request, or would be the first to.</summary>
public enum ProviderSpendScope
{
    None,
    ApiKey,
    Tenant,
}
