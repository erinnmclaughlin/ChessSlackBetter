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

/// <summary>
/// One turn of the game: a normal move, or (in dice games) a set of board edits in place of a move,
/// in which case <see cref="Edits"/> is set and <see cref="Move"/> is meaningless.
/// </summary>
public sealed record PlayedMove(Move Move, string San, IReadOnlyList<SquareEdit>? Edits = null)
{
    public const string DicePrefix = "dice:";

    public bool IsDiceRoll => Edits is not null;

    /// <summary>Storage form: UCI for moves ("e2e4"), "dice:e4=B,d5=-" for edits.</summary>
    public string ToToken() => Edits is null ? Move.ToUci() : DicePrefix + string.Join(',', Edits);
}

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

    /// <summary>Rebuilds a game from stored tokens (see <see cref="PlayedMove.ToToken"/>), e.g. "e2e4", "dice:e4=B".</summary>
    public static Game Replay(IEnumerable<string> tokens)
    {
        var game = new Game();
        foreach (var token in tokens)
        {
            string? error;
            var ok = token.StartsWith(PlayedMove.DicePrefix, StringComparison.Ordinal)
                ? game.TryApplyEdits(token[PlayedMove.DicePrefix.Length..].Split(',').Select(SquareEdit.Parse).ToList(), out error)
                : game.TryPlay(token, out _, out error);
            if (!ok)
            {
                throw new FormatException($"Stored move '{token}' is invalid: {error}");
            }
        }

        return game;
    }

    /// <summary>
    /// Checks whether board edits may replace the current player's move: kings can't be touched or created,
    /// pawns can't land on the back ranks, the player can't leave themself in check, and the opponent must
    /// still have a legal move (no checkmate or stalemate by dice).
    /// </summary>
    public static bool ValidateEdits(Position position, IReadOnlyList<SquareEdit> edits, [NotNullWhen(false)] out string? error)
    {
        error = null;
        if (edits.Count == 0)
        {
            error = "Nothing on the board would change.";
            return false;
        }

        foreach (var edit in edits)
        {
            if (position[edit.Square].Type == PieceType.King || edit.Piece.Type == PieceType.King)
            {
                error = "Kings can't be changed by dice.";
                return false;
            }

            // A pawn on its own back rank can still move forward; one on its promotion rank can't move at all.
            var promotionRank = edit.Piece.Color == PieceColor.White ? 7 : 0;
            if (edit.Piece.Type == PieceType.Pawn && Square.Rank(edit.Square) == promotionRank)
            {
                error = "Pawns can't be placed on the rank where they would promote.";
                return false;
            }
        }

        var after = position.WithEdits(edits);
        if (after.IsInCheck(position.SideToMove))
        {
            error = "That would leave your own king in check.";
            return false;
        }

        if (after.GenerateLegalMoves().Count == 0)
        {
            error = after.IsInCheck()
                ? "Dice can't deliver checkmate."
                : "That would stalemate your opponent.";
            return false;
        }

        return true;
    }

    /// <summary>Applies dice edits in place of the current player's move.</summary>
    public bool TryApplyEdits(IReadOnlyList<SquareEdit> edits, [NotNullWhen(false)] out string? error)
    {
        if (IsOver)
        {
            error = "The game is already over.";
            return false;
        }

        if (!ValidateEdits(Current, edits, out error))
        {
            return false;
        }

        _moves.Add(new PlayedMove(default, "{dice " + string.Join(' ', edits) + "}", edits));
        Current = Current.WithEdits(edits);
        _repetitionKeys.Add(Current.RepetitionKey());
        DetectAutomaticEnd();
        return true;
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
