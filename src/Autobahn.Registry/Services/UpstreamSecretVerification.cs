using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;
using Autobahn.Domain.Models;
using Autobahn.Application.Security;

namespace Autobahn.Registry.Services;
/// <summary>Result of checking the stored credentials against the models that use them.</summary>
/// <param name = "Total">Stored credentials a model points at.</param>
/// <param name = "Undecryptable">Model ids among those whose credential does not decrypt.</param>
/// <param name = "Unreferenced">Stored ids no model points at; left out of the other two.</param>
public sealed record UpstreamSecretVerification(int Total, IReadOnlyList<string> Undecryptable, IReadOnlyList<string> Unreferenced);
