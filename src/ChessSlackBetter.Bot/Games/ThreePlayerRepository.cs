using System.Text.Json;
using ChessSlackBetter.Bot.Dice;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace ChessSlackBetter.Bot.Games;

public interface IThreePlayerRepository
{
    Task<ThreePlayerRecord?> GetAsync(string channelId, string threadTs, CancellationToken ct = default);

    Task SaveAsync(ThreePlayerRecord game, CancellationToken ct = default);

    /// <summary>Games the user is in that are waiting to be accepted or still being played.</summary>
    Task<IReadOnlyList<ThreePlayerRecord>> GetOpenGamesAsync(string userId, CancellationToken ct = default);

    Task<IReadOnlyList<ThreePlayerRecord>> GetFinishedGamesAsync(CancellationToken ct = default);
}

/// <summary>Three-player games live in their own table next to the two-player <c>games</c> table.</summary>
public sealed class SqliteThreePlayerRepository : IThreePlayerRepository
{
    private const string Columns =
        "channel_id, thread_ts, challenger_id, white_id, black_id, red_id, state, accepted, declined_by, moves, draw_agreed, created_at, updated_at, with_dice, dice";

    private readonly string _connectionString;

    public SqliteThreePlayerRepository(IOptions<ChessBotOptions> options)
        : this(options.Value.DatabasePath)
    {
    }

    public SqliteThreePlayerRepository(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS three_player_games (
                channel_id    TEXT NOT NULL,
                thread_ts     TEXT NOT NULL,
                challenger_id TEXT NOT NULL,
                white_id      TEXT NOT NULL,
                black_id      TEXT NOT NULL,
                red_id        TEXT NOT NULL,
                state         TEXT NOT NULL,
                accepted      TEXT NOT NULL,
                declined_by   TEXT,
                moves         TEXT NOT NULL,
                draw_agreed   TEXT NOT NULL,
                created_at    TEXT NOT NULL,
                updated_at    TEXT NOT NULL,
                PRIMARY KEY (channel_id, thread_ts)
            );
            CREATE INDEX IF NOT EXISTS ix_three_player_state ON three_player_games (state);
            """;
        command.ExecuteNonQuery();

        // Columns added after the first release; older databases get them here.
        AddColumnIfMissing(connection, "with_dice", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "dice", "TEXT");
    }

    public async Task<ThreePlayerRecord?> GetAsync(string channelId, string threadTs, CancellationToken ct = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM three_player_games WHERE channel_id = $channel AND thread_ts = $thread";
        command.Parameters.AddWithValue("$channel", channelId);
        command.Parameters.AddWithValue("$thread", threadTs);
        return (await ReadAllAsync(command, ct)).FirstOrDefault();
    }

    public async Task SaveAsync(ThreePlayerRecord game, CancellationToken ct = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO three_player_games ({Columns})
            VALUES ($channel, $thread, $challenger, $white, $black, $red, $state, $accepted, $declined, $moves, $draw, $created, $updated, $withDice, $dice)
            ON CONFLICT (channel_id, thread_ts) DO UPDATE SET
                state = excluded.state,
                dice = excluded.dice,
                accepted = excluded.accepted,
                declined_by = excluded.declined_by,
                moves = excluded.moves,
                draw_agreed = excluded.draw_agreed,
                updated_at = excluded.updated_at
            """;
        command.Parameters.AddWithValue("$channel", game.ChannelId);
        command.Parameters.AddWithValue("$thread", game.ThreadTs);
        command.Parameters.AddWithValue("$challenger", game.ChallengerId);
        command.Parameters.AddWithValue("$white", game.WhiteId);
        command.Parameters.AddWithValue("$black", game.BlackId);
        command.Parameters.AddWithValue("$red", game.RedId);
        command.Parameters.AddWithValue("$state", game.State.ToString());
        command.Parameters.AddWithValue("$accepted", string.Join(' ', game.Accepted));
        command.Parameters.AddWithValue("$declined", (object?)game.DeclinedBy ?? DBNull.Value);
        command.Parameters.AddWithValue("$moves", string.Join(' ', game.Moves));
        command.Parameters.AddWithValue("$draw", string.Join(' ', game.DrawAgreed));
        command.Parameters.AddWithValue("$created", game.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated", game.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$withDice", game.WithDice ? 1 : 0);
        command.Parameters.AddWithValue("$dice", game.WithDice ? JsonSerializer.Serialize(game.Dice) : (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<ThreePlayerRecord>> GetOpenGamesAsync(string userId, CancellationToken ct = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {Columns} FROM three_player_games
            WHERE (white_id = $user OR black_id = $user OR red_id = $user) AND state IN ('Challenged', 'Active')
            ORDER BY updated_at DESC
            """;
        command.Parameters.AddWithValue("$user", userId);
        return await ReadAllAsync(command, ct);
    }

    public async Task<IReadOnlyList<ThreePlayerRecord>> GetFinishedGamesAsync(CancellationToken ct = default)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM three_player_games WHERE state = 'Finished'";
        return await ReadAllAsync(command, ct);
    }

    private static void AddColumnIfMissing(SqliteConnection connection, string column, string definition)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('three_player_games') WHERE name = $name";
        check.Parameters.AddWithValue("$name", column);
        if (Convert.ToInt64(check.ExecuteScalar()) > 0)
        {
            return;
        }

        using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE three_player_games ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static List<string> Words(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static async Task<List<ThreePlayerRecord>> ReadAllAsync(SqliteCommand command, CancellationToken ct)
    {
        var results = new List<ThreePlayerRecord>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new ThreePlayerRecord
            {
                ChannelId = reader.GetString(0),
                ThreadTs = reader.GetString(1),
                ChallengerId = reader.GetString(2),
                WhiteId = reader.GetString(3),
                BlackId = reader.GetString(4),
                RedId = reader.GetString(5),
                State = Enum.Parse<GameState>(reader.GetString(6)),
                Accepted = Words(reader.GetString(7)),
                DeclinedBy = reader.IsDBNull(8) ? null : reader.GetString(8),
                Moves = Words(reader.GetString(9)),
                DrawAgreed = Words(reader.GetString(10)),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(11)),
                UpdatedAt = DateTimeOffset.Parse(reader.GetString(12)),
                WithDice = reader.GetInt64(13) != 0,
                Dice = reader.IsDBNull(14) ? new DiceState() : JsonSerializer.Deserialize<DiceState>(reader.GetString(14)) ?? new DiceState(),
            });
        }

        return results;
    }
}
