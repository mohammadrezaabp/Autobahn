using System.Text.RegularExpressions;
using Autobahn.Domain.Common;

namespace Autobahn.Domain.Fleet;

public static class ModelReference
{
    public static void Validate(string value)
    {
        if (value.Contains('/')) { RepositoryId.Validate(value); return; }
        if (!Regex.IsMatch(value, "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$") || value.Contains("..", StringComparison.Ordinal))
            throw new DomainException("invalid_model", "Choose a model repository or a directory in the model store.");
    }
}
