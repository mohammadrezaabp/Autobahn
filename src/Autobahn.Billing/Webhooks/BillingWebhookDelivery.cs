using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Autobahn.Application.Abstractions;
using Autobahn.Domain.Configuration;

namespace Autobahn.Billing.Webhooks;
/// <summary>A queued webhook awaiting delivery by <see cref = "BillingWebhookSenderHostedService"/>.</summary>
internal sealed record BillingWebhookDelivery(string EventType, string Body, Action? OnPermanentFailure);
