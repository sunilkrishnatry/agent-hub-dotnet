using Microsoft.Extensions.Logging;
using Npgsql;

namespace AgentHub.Persistence;

public sealed class PostgresConversationHistoryRepository : IConversationHistoryRepository
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresConversationHistoryRepository> _logger;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _isInitialized;

    public PostgresConversationHistoryRepository(
        PostgresConversationOptions options,
        ILogger<PostgresConversationHistoryRepository> logger)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "PostgreSQL connection string is empty. Verify AgentHub:Postgres configuration.");
        }

        _connectionString = options.ConnectionString;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ConversationMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string sql = """
            SELECT id, conversation_id, role, content, created_at
            FROM conversation_messages
            WHERE conversation_id = @conversationId
            ORDER BY id;
            """;

        _logger.LogDebug("Fetching messages for conversation {ConversationId}", conversationId);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("conversationId", conversationId);

            var messages = new List<ConversationMessage>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                messages.Add(new ConversationMessage(
                    Id: reader.GetInt64(0),
                    ConversationId: reader.GetGuid(1),
                    Role: reader.GetString(2),
                    Content: reader.GetString(3),
                    CreatedAt: reader.GetFieldValue<DateTimeOffset>(4)));
            }

            _logger.LogDebug("Fetched {Count} messages for conversation {ConversationId}", messages.Count, conversationId);
            return messages;
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex,
                "PostgreSQL error fetching messages for conversation {ConversationId}. SqlState={SqlState}",
                conversationId, ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error fetching messages for conversation {ConversationId}",
                conversationId);
            throw;
        }
    }

    public async Task AppendMessageAsync(
        Guid conversationId,
        string role,
        string content,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        const string conversationSql = """
            INSERT INTO conversations (id, created_at)
            VALUES (@conversationId, @createdAt)
            ON CONFLICT (id) DO NOTHING;
            """;

        const string messageSql = """
            INSERT INTO conversation_messages (conversation_id, role, content, created_at)
            VALUES (@conversationId, @role, @content, @createdAt);
            """;

        _logger.LogDebug("Appending {Role} message to conversation {ConversationId}", role, conversationId);

        try
        {
            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var conversationCommand = new NpgsqlCommand(conversationSql, connection))
            {
                conversationCommand.Parameters.AddWithValue("conversationId", conversationId);
                conversationCommand.Parameters.AddWithValue("createdAt", createdAt);
                await conversationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using var messageCommand = new NpgsqlCommand(messageSql, connection);
            messageCommand.Parameters.AddWithValue("conversationId", conversationId);
            messageCommand.Parameters.AddWithValue("role", role);
            messageCommand.Parameters.AddWithValue("content", content);
            messageCommand.Parameters.AddWithValue("createdAt", createdAt);
            await messageCommand.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogDebug("Appended {Role} message to conversation {ConversationId}", role, conversationId);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogError(ex,
                "PostgreSQL error appending {Role} message to conversation {ConversationId}. SqlState={SqlState}",
                role, conversationId, ex.SqlState);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error appending {Role} message to conversation {ConversationId}",
                role, conversationId);
            throw;
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            _logger.LogInformation("Initializing PostgreSQL schema (conversations, conversation_messages)");

            const string sql = """
                CREATE TABLE IF NOT EXISTS conversations (
                    id UUID PRIMARY KEY,
                    created_at TIMESTAMPTZ NOT NULL
                );

                CREATE TABLE IF NOT EXISTS conversation_messages (
                    id BIGSERIAL PRIMARY KEY,
                    conversation_id UUID NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                    role TEXT NOT NULL,
                    content TEXT NOT NULL,
                    created_at TIMESTAMPTZ NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_conversation_messages_conversation_id
                    ON conversation_messages (conversation_id, id);
                """;

            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new NpgsqlCommand(sql, connection);
                await command.ExecuteNonQueryAsync(cancellationToken);

                _isInitialized = true;
                _logger.LogInformation("PostgreSQL schema initialized successfully. Host={Host}", connection.Host);
            }
            catch (NpgsqlException ex)
            {
                _logger.LogError(ex,
                    "Failed to initialize PostgreSQL schema. SqlState={SqlState} — " +
                    "Verify Host, Port, Database, Username, Password and that the server is reachable.",
                    ex.SqlState);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Unexpected error during PostgreSQL schema initialization. " +
                    "Check that the connection string is valid and the server is reachable.");
                throw;
            }
        }
        finally
        {
            _initializationGate.Release();
        }
    }
}
