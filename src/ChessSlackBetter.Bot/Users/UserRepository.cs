using Microsoft.Extensions.Options;

namespace ChessSlackBetter.Bot.Users;

/// <summary>Per-user preferences. Users are keyed by Slack user id.</summary>
public interface IUserRepository
{
    /// <summary>The user's board theme name, or null if they haven't picked one.</summary>
    Task<string?> GetThemeAsync(string userId, CancellationToken ct = default);

    Task SetThemeAsync(string userId, string theme, CancellationToken ct = default);
}

public sealed class SqliteUserRepository : IUserRepository
{
    private readonly string _connectionString;
    private readonly TimeProvider _time;

    public SqliteUserRepository(IOptions<ChessBotOptions> options, TimeProvider time)
        : this(options.Value.DatabasePath, time)
    {
    }

    public SqliteUserRepository(string databasePath, TimeProvider? time = null)
    {
        _connectionString = Database.ConnectionString(databasePath);
        _time = time ?? TimeProvider.System;
        Database.EnsureSchema(_connectionString);
    }

    public async Task<string?> GetThemeAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = Database.Open(_connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT theme FROM users WHERE user_id = $user";
        command.Parameters.AddWithValue("$user", userId);
        return await command.ExecuteScalarAsync(ct) as string;
    }

    public async Task SetThemeAsync(string userId, string theme, CancellationToken ct = default)
    {
        // The user may not have played yet, so this can create their row.
        await using var connection = Database.Open(_connectionString);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (user_id, created_at, theme) VALUES ($user, $created, $theme)
            ON CONFLICT (user_id) DO UPDATE SET theme = excluded.theme
            """;
        command.Parameters.AddWithValue("$user", userId);
        command.Parameters.AddWithValue("$created", _time.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$theme", theme);
        await command.ExecuteNonQueryAsync(ct);
    }
}
