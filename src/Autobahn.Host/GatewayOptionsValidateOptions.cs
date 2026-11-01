using Microsoft.Extensions.Options;
using Autobahn.Domain.Configuration;

namespace Autobahn.Host;

internal sealed class GatewayOptionsValidateOptions : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        var errors = GatewayOptionsValidation.Validate(options);
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
