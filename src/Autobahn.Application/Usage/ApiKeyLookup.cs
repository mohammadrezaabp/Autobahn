using Autobahn.Application.Abstractions;
using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Usage;
public sealed record ApiKeyLookup(string KeyPrefix, string? Assignee);
