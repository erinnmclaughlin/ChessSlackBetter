using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ChessSlackBetter.Chess;

public enum GameOutcome
{
    Ongoing,
    WhiteWins,
    BlackWins,
    Draw,
}

public enum Termination
{
    None,
    Checkmate,
    Resignation,
    Stalemate,
    DrawAgreement,
    ThreefoldRepetition,
    FiftyMoveRule,
    InsufficientMaterial,
    Abandoned,
}

public sealed record PlayedMove(Move Move, string San);

/// <summary>A game from the standard starting position: its moves and how (or whether) it ended.</summary>
public sealed class Game
{
    private readonly List<PlayedMove> _moves = [];
    private readonly List<string> _repetitionKeys = [];

    public Game()
    {
        Current = Position.Start;
        _repetitionKeys.Add(Current.RepetitionKey());
    }

    public Position Current { get; private set; }

    public IReadOnlyList<PlayedMove> Moves => _moves;

    public PlayedMove? LastMove => _moves.Count == 0 ? null : _moves[^1];

    public GameOutcome Outcome { get; private set; } = GameOutcome.Ongoing;

    public Termination Termination { get; private set; } = Termination.None;

    public bool IsOver => Outcome != GameOutcome.Ongoing;

    /// <summary>Rebuilds a game from a list of UCI moves, e.g. "e2e4 e7e5".</summary>
    public static Game FromUci(IEnumerable<string> uciMoves)
    {
        var game = new Game();
        foreach (var uci in uciMoves)
        {
            if (!game.TryPlay(uci, out _, out var error))
            {
                throw new FormatException($"Stored move '{uci}' is invalid: {error}");
            }
        }

        return game;
    }

    public bool TryPlay(string input, [NotNullWhen(true)] out PlayedMove? played, [NotNullWhen(false)] out string? error)
    {
        played = null;
        if (IsOver)
        {
            error = "The game is already over.";
            return false;
        }

        if (!Notation.TryParseMove(Current, input, out var move, out error))
        {
            return false;
        }

        played = new PlayedMove(move, Notation.ToSan(Current, move));
        _moves.Add(played);
        Current = Current.Apply(move);
        _repetitionKeys.Add(Current.RepetitionKey());
        DetectAutomaticEnd();
        return true;
    }

    public void Resign(PieceColor loser) =>
        End(loser == PieceColor.White ? GameOutcome.BlackWins : GameOutcome.WhiteWins, Termination.Resignation);

    public void AgreeDraw() => End(GameOutcome.Draw, Termination.DrawAgreement);

    public void Abandon() => End(GameOutcome.Draw, Termination.Abandoned);

    public string ResultString => Outcome switch
    {
        GameOutcome.WhiteWins => "1-0",
        GameOutcome.BlackWins => "0-1",
        GameOutcome.Draw => "1/2-1/2",
        _ => "*",
    };

    public string MovesSan()
    {
        var sb = new StringBuilder();
        for (var i = 0; i < _moves.Count; i++)
        {
            if (i % 2 == 0)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }

                sb.Append(i / 2 + 1).Append(". ");
            }
            else
            {
                sb.Append(' ');
            }

            sb.Append(_moves[i].San);
        }

        return sb.ToString();
    }

    public string ToPgn(IReadOnlyDictionary<string, string>? headers = null)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in headers ?? new Dictionary<string, string>())
        {
            sb.Append('[').Append(key).Append(" \"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).AppendLine("\"]");
        }

        sb.Append("[Result \"").Append(ResultString).AppendLine("\"]");
        sb.AppendLine();
        var moves = MovesSan();
        sb.Append(moves.Length == 0 ? ResultString : moves + " " + ResultString);
        return sb.ToString();
    }

    private void DetectAutomaticEnd()
    {
        var hasMoves = Current.GenerateLegalMoves().Count > 0;
        if (!hasMoves)
        {
            if (Current.IsInCheck())
            {
                End(Current.SideToMove == PieceColor.White ? GameOutcome.BlackWins : GameOutcome.WhiteWins, Termination.Checkmate);
            }
            else
            {
                End(GameOutcome.Draw, Termination.Stalemate);
            }
        }
        else if (Current.IsInsufficientMaterial())
        {
            End(GameOutcome.Draw, Termination.InsufficientMaterial);
        }
        else if (_repetitionKeys.Count(k => k == _repetitionKeys[^1]) >= 3)
        {
            End(GameOutcome.Draw, Termination.ThreefoldRepetition);
        }
        else if (Current.HalfmoveClock >= 100)
        {
            End(GameOutcome.Draw, Termination.FiftyMoveRule);
        }
    }

    private void End(GameOutcome outcome, Termination termination)
    {
        if (IsOver)
        {
            throw new InvalidOperationException("The game is already over.");
        }

        Outcome = outcome;
        Termination = termination;
    }
}
