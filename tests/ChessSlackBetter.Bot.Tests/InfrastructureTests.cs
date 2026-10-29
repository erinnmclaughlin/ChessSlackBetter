using System.Text.Json.Nodes;
using ChessSlackBetter.Bot.Games;
using ChessSlackBetter.Bot.Rendering;
using ChessSlackBetter.Bot.Slack;
using ChessSlackBetter.Chess;
using Microsoft.Extensions.Options;

namespace ChessSlackBetter.Bot.Tests;

public class CommandParserTests
{
    [Theory]
    [InlineData("challenge <@UBOB>", typeof(ChallengeCommand))]
    [InlineData("play <@UBOB|bob> black", typeof(ChallengeCommand))]
    [InlineData("accept", typeof(AcceptCommand))]
    [InlineData("decline", typeof(DeclineCommand))]
    [InlineData("decline draw", typeof(DeclineDrawCommand))]
    [InlineData("resign", typeof(ResignCommand))]
    [InlineData("draw", typeof(DrawCommand))]
    [InlineData("e2e4", typeof(MoveCommand))]
    [InlineData("Nf3", typeof(MoveCommand))]
    [InlineData("move e4", typeof(MoveCommand))]
    [InlineData("O-O", typeof(MoveCommand))]
    [InlineData("board", typeof(BoardCommand))]
    [InlineData("pgn", typeof(PgnCommand))]
    [InlineData("stats", typeof(StatsCommand))]
    [InlineData("leaderboard", typeof(LeaderboardCommand))]
    [InlineData("", typeof(HelpCommand))]
    [InlineData("help", typeof(HelpCommand))]
    [InlineData("challenge", typeof(UnknownCommand))]
    [InlineData("what is love", typeof(UnknownCommand))]
    public void Parses(string text, Type expected)
    {
        Assert.IsType(expected, CommandParser.Parse(text));
    }

    [Fact]
    public void Challenge_reads_opponent_and_colour()
    {
        var command = Assert.IsType<ChallengeCommand>(CommandParser.Parse("challenge <@UBOB|bob> as Black"));
        Assert.Equal("UBOB", command.OpponentId);
        Assert.Equal(PieceColor.Black, command.ChallengerColor);
    }

    [Fact]
    public void Strips_the_bot_mention()
    {
        Assert.Equal("e4", CommandParser.StripMention("<@UBOT> e4", "UBOT"));
        Assert.Equal("challenge <@UBOB>", CommandParser.StripMention("<@UBOT|chessbot>  challenge <@UBOB>", "UBOT"));
    }
}

public class SlackSignatureVerifierTests
{
    private const string Secret = "8f742231b10e8888abcd99yyyzzz85a5";

    [Fact]
    public void Accepts_valid_and_rejects_tampered_requests()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var verifier = new SlackSignatureVerifier(Options.Create(new SlackOptions { SigningSecret = Secret }), new FixedTime(now));
        var ts = now.ToUnixTimeSeconds().ToString();
        const string body = "token=x&payload=%7B%7D";
        var signature = SlackSignatureVerifier.Sign(Secret, ts, body);

        Assert.True(verifier.IsValid(ts, signature, body));
        Assert.False(verifier.IsValid(ts, signature, body + "x"));
        Assert.False(verifier.IsValid(ts, "v0=deadbeef", body));
        Assert.False(verifier.IsValid(null, signature, body));
    }

    [Fact]
    public void Rejects_old_timestamps()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        var verifier = new SlackSignatureVerifier(Options.Create(new SlackOptions { SigningSecret = Secret }), new FixedTime(now));
        var old = now.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        Assert.False(verifier.IsValid(old, SlackSignatureVerifier.Sign(Secret, old, "{}"), "{}"));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

public class SlackEventParsingTests
{
    [Fact]
    public void Parses_mentions_and_thread_messages()
    {
        var mention = SlackEndpoints.ParseMessage(JsonNode.Parse("""
            {"type":"app_mention","user":"U1","text":"<@UBOT> e4","ts":"2.0","thread_ts":"1.0","channel":"C1"}
            """));
        Assert.Equal(new IncomingMessage("C1", "U1", "<@UBOT> e4", "2.0", "1.0", true), mention);

        var message = SlackEndpoints.ParseMessage(JsonNode.Parse("""
            {"type":"message","user":"U1","text":"e4","ts":"2.0","channel":"C1"}
            """));
        Assert.False(message!.IsMention);
    }

    [Theory]
    [InlineData("""{"type":"message","subtype":"message_changed","channel":"C1","ts":"1.0"}""")]
    [InlineData("""{"type":"message","bot_id":"B1","user":"U1","text":"hi","ts":"1.0","channel":"C1"}""")]
    [InlineData("""{"type":"reaction_added","user":"U1"}""")]
    public void Ignores_other_events(string json)
    {
        Assert.Null(SlackEndpoints.ParseMessage(JsonNode.Parse(json)));
    }
}

public class BoardRendererTests
{
    [Fact]
    public void Renders_a_png()
    {
        using var renderer = new BoardRenderer();
        var position = Position.FromFen("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");
        var png = renderer.RenderPng(position, new Move(Square.Parse("d8"), Square.Parse("h4")), PieceColor.Black);

        Assert.True(png.Length > 1000);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png[..4]);
    }

    [Fact]
    public void Text_board_respects_perspective()
    {
        var white = BoardRenderer.RenderText(Position.Start, PieceColor.White);
        var black = BoardRenderer.RenderText(Position.Start, PieceColor.Black);
        Assert.StartsWith("8 r n b q k b n r", white);
        Assert.StartsWith("1 R N B K Q B N R", black);
    }
}

public sealed class SqliteGameRepositoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"chessbot-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Round_trips_games()
    {
        var repo = new SqliteGameRepository(_path);
        var now = DateTimeOffset.UtcNow;
        var record = new GameRecord
        {
            ChannelId = "C1",
            ThreadTs = "1.0",
            ChallengerId = "UA",
            OpponentId = "UB",
            WhiteId = "UA",
            BlackId = "UB",
            State = GameState.Active,
            Moves = ["e2e4", "e7e5"],
            DrawOfferedBy = "UA",
            CreatedAt = now,
            UpdatedAt = now,
        };
        await repo.SaveAsync(record);

        record.Moves.Add("g1f3");
        record.DrawOfferedBy = null;
        await repo.SaveAsync(record);

        var loaded = await repo.GetAsync("C1", "1.0");
        Assert.NotNull(loaded);
        Assert.Equal(["e2e4", "e7e5", "g1f3"], loaded.Moves);
        Assert.Null(loaded.DrawOfferedBy);
        Assert.Single(await repo.GetOpenGamesAsync("UB"));
        Assert.Empty(await repo.GetFinishedGamesAsync());
        Assert.Null(await repo.GetAsync("C1", "2.0"));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}
