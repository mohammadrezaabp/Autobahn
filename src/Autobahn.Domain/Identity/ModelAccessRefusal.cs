using Autobahn.Domain.Models;

namespace Autobahn.Domain.Identity;
/// <summary>The rule that refused a key a model, in the order the rules are checked.</summary>
public enum ModelAccessRefusal
{
    None,
    ProviderClosedForTenant,
    ProviderClosedForKey,
    ModelNotOnTenantList,
    ModelNotOnKeyList,
}
