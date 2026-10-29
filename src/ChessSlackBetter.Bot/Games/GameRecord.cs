using ChessSlackBetter.Chess;

namespace ChessSlackBetter.Bot.Games;

public enum GameState
{
    /// <summary>Waiting for the challenged player to accept.</summary>
    Challenged,
    Active,
    Finished,

    /// <summary>The challenge was declined or withdrawn; no game was played.</summary>
    Declined,
}

/// <summary>
/// A game as stored in the database. Each game lives in the Slack thread under the bot's challenge message,
/// so (ChannelId, ThreadTs) identifies it.
/// </summary>
public sealed class GameRecord
{
    public required string ChannelId { get; init; }

    public required string ThreadTs { get; init; }

    public required string ChallengerId { get; init; }

    public required string OpponentId { get; init; }

    public required string WhiteId { get; init; }

    public required string BlackId { get; init; }

    public GameState State { get; set; } = GameState.Challenged;

    /// <summary>Moves in UCI notation, e.g. "e2e4".</summary>
    public List<string> Moves { get; set; } = [];

    public string? DrawOfferedBy { get; set; }

    public GameOutcome Outcome { get; set; } = GameOutcome.Ongoing;

    public Termination Termination { get; set; } = Termination.None;

    /// <summary>Lichess analysis URL, set once a finished game has been imported.</summary>
    public string? AnalysisUrl { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }

    public bool IsPlayer(string userId) => userId == WhiteId || userId == BlackId;

    public PieceColor ColorOf(string userId) =>
        userId == WhiteId ? PieceColor.White :
        userId == BlackId ? PieceColor.Black :
        throw new InvalidOperationException($"{userId} is not playing in this game.");

    public string PlayerFor(PieceColor color) => color == PieceColor.White ? WhiteId : BlackId;

    public string OpponentOf(string userId) => userId == WhiteId ? BlackId : WhiteId;

    /// <summary>Replays the stored moves, including any result that isn't implied by the moves themselves.</summary>
    public Game LoadGame()
    {
        var game = Game.FromUci(Moves);
        if (!game.IsOver)
        {
            switch (Termination)
            {
                case Termination.Resignation:
                    game.Resign(Outcome == GameOutcome.WhiteWins ? PieceColor.Black : PieceColor.White);
                    break;
                case Termination.DrawAgreement:
                    game.AgreeDraw();
                    break;
                case Termination.Abandoned:
                    game.Abandon();
                    break;
            }
        }

        return game;
    }

    /// <summary>Copies the result of a game into this record.</summary>
    public void SyncFrom(Game game)
    {
        Moves = game.Moves.Select(m => m.Move.ToUci()).ToList();
        Outcome = game.Outcome;
        Termination = game.Termination;
        if (game.IsOver)
        {
            State = GameState.Finished;
            DrawOfferedBy = null;
        }
    }
}
