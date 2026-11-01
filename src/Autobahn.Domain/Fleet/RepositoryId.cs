using System.Text.RegularExpressions;
using Autobahn.Domain.Common;

namespace Autobahn.Domain.Fleet;

public static class RepositoryId
{
    public static void Validate(string value)
    {
        if (!Regex.IsMatch(value ?? "", "^[A-Za-z0-9][A-Za-z0-9._-]{0,95}/[A-Za-z0-9][A-Za-z0-9._-]{0,95}$") || value!.Contains("..", StringComparison.Ordinal))
            throw new DomainException("invalid_repository", "Use an owner/model repository identifier.");
    }
}
