using System.Text.Json.Nodes;
using Autobahn.Application.Fleet;
using Microsoft.Data.Sqlite;

namespace Autobahn.Infrastructure.Fleet;

public sealed class SqliteFleetStateStore : IFleetStateStore
{
    private readonly string _connectionString;
    public SqliteFleetStateStore(string directory)
    {
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(directory, "fleet.db"), DefaultTimeout = 30 }.ToString();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS documents (collection TEXT NOT NULL, id TEXT NOT NULL, value TEXT NOT NULL, updated TEXT NOT NULL, PRIMARY KEY(collection,id));";
        command.ExecuteNonQuery();
    }

    public async Task<JsonNode?> ReadAsync(string collection, string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM documents WHERE collection=$collection AND id=$id";
        command.Parameters.AddWithValue("$collection", collection); command.Parameters.AddWithValue("$id", id);
        return await command.ExecuteScalarAsync(cancellationToken) is string value ? JsonNode.Parse(value) : null;
    }

    public async Task<IReadOnlyList<JsonNode>> ListAsync(string collection, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM documents WHERE collection=$collection ORDER BY updated DESC LIMIT 10000";
        command.Parameters.AddWithValue("$collection", collection);
        var result = new List<JsonNode>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(JsonNode.Parse(reader.GetString(0))!);
        return result;
    }

    public async Task WriteAsync(string collection, string id, JsonNode value, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO documents VALUES($collection,$id,$value,$updated) ON CONFLICT(collection,id) DO UPDATE SET value=excluded.value,updated=excluded.updated";
        command.Parameters.AddWithValue("$collection", collection); command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$value", value.ToJsonString()); command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string collection, string id, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString); await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM documents WHERE collection=$collection AND id=$id";
        command.Parameters.AddWithValue("$collection", collection); command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
