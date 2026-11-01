using System.Collections.Concurrent;
using Autobahn.Domain.Models;

namespace Autobahn.Application.Providers;
/// <summary>How much of its monthly cap a tenant has used at a provider.</summary>
public sealed record ProviderTenantCapUsage(string Provider, string TenantSlug, double Ratio);
