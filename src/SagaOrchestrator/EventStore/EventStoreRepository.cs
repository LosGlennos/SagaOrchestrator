using Npgsql;
using System.Text.Json;

namespace SagaOrchestrator.EventStore;

public class EventStoreRepository
{
    private readonly string _connectionString;

    public EventStoreRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var createTableSql = @"
            CREATE TABLE IF NOT EXISTS events (
                event_id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
                aggregate_id UUID NOT NULL,
                event_type VARCHAR(255) NOT NULL,
                event_data JSONB NOT NULL,
                timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                version INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_events_aggregate_id ON events(aggregate_id);
            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp);
        ";

        await using var command = new NpgsqlCommand(createTableSql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<Guid> AppendEventAsync<T>(Guid aggregateId, string eventType, T eventData, int version)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var eventId = Guid.NewGuid();
        var eventDataJson = JsonSerializer.Serialize(eventData);

        var sql = @"
            INSERT INTO events (event_id, aggregate_id, event_type, event_data, version)
            VALUES (@eventId, @aggregateId, @eventType, @eventData::jsonb, @version)
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("aggregateId", aggregateId);
        command.Parameters.AddWithValue("eventType", eventType);
        command.Parameters.AddWithValue("eventData", eventDataJson);
        command.Parameters.AddWithValue("version", version);

        await command.ExecuteNonQueryAsync();
        return eventId;
    }

    public async Task<List<EventRecord>> GetEventsByAggregateIdAsync(Guid aggregateId)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT event_id, aggregate_id, event_type, event_data::text, timestamp, version
            FROM events
            WHERE aggregate_id = @aggregateId
            ORDER BY version ASC
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("aggregateId", aggregateId);

        await using var reader = await command.ExecuteReaderAsync();
        var events = new List<EventRecord>();

        while (await reader.ReadAsync())
        {
            // Get event_data as text (already converted in SQL query)
            string eventDataString = reader.IsDBNull(3) ? "{}" : reader.GetString(3) ?? "{}";

            events.Add(new EventRecord
            {
                EventId = reader.GetGuid(0),
                AggregateId = reader.GetGuid(1),
                EventType = reader.GetString(2),
                EventData = eventDataString,
                Timestamp = reader.GetDateTime(4),
                Version = reader.GetInt32(5)
            });
        }

        return events;
    }

    public async Task<List<Guid>> GetAllAggregateIdsAsync()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        var sql = @"
            SELECT DISTINCT aggregate_id
            FROM events
            ORDER BY aggregate_id
        ";

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var aggregateIds = new List<Guid>();

        while (await reader.ReadAsync())
        {
            aggregateIds.Add(reader.GetGuid(0));
        }

        return aggregateIds;
    }
}

public class EventRecord
{
    public Guid EventId { get; set; }
    public Guid AggregateId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string EventData { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int Version { get; set; }
}

