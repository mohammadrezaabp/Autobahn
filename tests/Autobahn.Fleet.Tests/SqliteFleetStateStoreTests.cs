using System.Text.Json.Nodes;
using Autobahn.Infrastructure.Fleet;
using Microsoft.Data.Sqlite;

namespace Autobahn.Fleet.Tests;

public sealed class SqliteFleetStateStoreTests
{
    [Fact]
    public async Task ConcurrentWritesRemainDurableAndCollectionsStayIsolated()
    {
        var directory = Path.Combine(Path.GetTempPath(), "autobahn-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SqliteFleetStateStore(directory);
            await Task.WhenAll(Enumerable.Range(0, 24).Select(i => store.WriteAsync("audit", i.ToString(), new JsonObject { ["value"] = i })));
            await store.WriteAsync("inventory", "0", new JsonObject { ["value"] = 100 });
            var reopened = new SqliteFleetStateStore(directory);
            (await reopened.ListAsync("audit")).Should().HaveCount(24);
            (await reopened.ReadAsync("inventory", "0"))!["value"]!.GetValue<int>().Should().Be(100);
            await reopened.DeleteAsync("inventory", "0"); (await reopened.ReadAsync("inventory", "0")).Should().BeNull();
            (await reopened.ListAsync("audit")).Should().HaveCount(24);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
