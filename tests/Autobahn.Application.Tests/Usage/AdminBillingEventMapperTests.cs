using Autobahn.Domain.Billing;
using Autobahn.Domain.Models;
using Autobahn.Application.Usage;

namespace Autobahn.Application.Tests.Usage;

public sealed class AdminBillingEventMapperTests
{
    [Fact]
    public void ToListItem_WithLookup_IncludesKeyPrefixAndAssignee()
    {
        var keyId = Guid.NewGuid();
        var record = new BillingEventRecord(
            Guid.NewGuid(),
            "req-1",
            Guid.NewGuid(),
            keyId,
            "gpt-4o",
            "eng",
            10,
            5,
            null,
            null,
            0.01m,
            100,
            DateTimeOffset.UtcNow);

        var item = AdminBillingEventMapper.ToListItem(
            record,
            new Dictionary<Guid, ApiKeyLookup>
            {
                [keyId] = new("sk-prefix", "Platform"),
            });

        item.KeyPrefix.Should().Be("sk-prefix");
        item.Assignee.Should().Be("Platform");
        item.ModelId.Should().Be("gpt-4o");
    }
}
