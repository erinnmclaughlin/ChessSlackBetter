using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace ChessSlackBetter.Chess.ThreePlayer;

/// <summary>
/// One turn: a normal move, or (in dice games) a set of board edits in place of a move, in which case
/// <see cref="Edits"/> is set and <see cref="Move"/> is meaningless.
/// </summary>
public sealed record TriTurn(TriColor Player, TriMove Move, string San, IReadOnlyList<TriSquareEdit>? Edits = null)
{
    public bool IsDiceRoll => Edits is not null;
}

/// <summary>A side knocked out of the game. <see cref="By"/> is who made the move that checkmated them.</summary>
public sealed record TriElimination(TriColor Player, Termination Reason, TriColor? By);

/// <summary>
/// A three-player game. Turns go White, Black, Red, skipping eliminated sides. A side with no legal move on its
/// turn is out if it's in check (checkmate); if not (stalemate) it just passes, unless only two sides are left,
/// in which case it's a draw. Eliminated sides' pieces leave the board, and the last side standing wins.
/// </summary>
public sealed class TriGame
{
    public const string DicePrefix = "dice:";
    public const string ResignPrefix = "resign:";
    public const string DrawToken = "draw";
    public const string AbortToken = "abort";

    private readonly List<string> _tokens = [];
    private readonly List<TriTurn> _moves = [];
    private readonly List<TriElimination> _eliminations = [];
    private readonly List<TriColor> _drawnBetween = [];

    /// <param name="start">Defaults to the normal starting position; others are for tests.</param>
    public TriGame(TriPosition? start = null)
    {
        Current = start ?? TriPosition.Start;
    }

    public TriPosition Current { get; private set; }

    /// <summary>What to store: moves ("d2d4"), dice turns ("dice:e4=wb,i5=-"), plus "resign:r", "draw" and "abort".</summary>
    public IReadOnlyList<string> Tokens => _tokens;

    public IReadOnlyList<TriTurn> Moves => _moves;

    public TriTurn? LastMove => _moves.Count == 0 ? null : _moves[^1];

    /// <summary>In the order they went out.</summary>
    public IReadOnlyList<TriElimination> Eliminations => _eliminations;

    public bool IsOver { get; private set; }

    /// <summary>How the game finished: the last elimination's reason, or how the draw came about.</summary>
    public Termination Termination { get; private set; } = Termination.None;

    /// <summary>Finishing places once the game is over, best first; sides that drew share a place.</summary>
    public IReadOnlyList<IReadOnlyList<TriColor>> Ranking
    {
        get
        {
            var places = new List<IReadOnlyList<TriColor>>();
            if (_drawnBetween.Count > 0)
            {
                places.Add(_drawnBetween.ToList());
            }
            else if (IsOver && Current.AliveColors.ToList() is { Count: 1 } winner)
            {
                places.Add(winner);
            }

            places.AddRange(_eliminations.AsEnumerable().Reverse().Select(e => (IReadOnlyList<TriColor>)[e.Player]));
            return places;
        }
    }

    /// <summary>True once a side is out, by checkmate or resignation.</summary>
    public bool IsEliminated(TriColor color) => _eliminations.Any(e => e.Player == color);

    public static TriGame Replay(IEnumerable<string> tokens)
    {
        var game = new TriGame();
        foreach (var token in tokens)
        {
            if (token.StartsWith(ResignPrefix, StringComparison.Ordinal) &&
                TriColors.FromLetter(token[^1]) is { } color)
            {
                game.Resign(color);
            }
            else if (token == DrawToken)
            {
                game.AgreeDraw();
            }
            else if (token == AbortToken)
            {
                game.Abandon();
            }
            else if (token.StartsWith(DicePrefix, StringComparison.Ordinal))
            {
                var edits = token[DicePrefix.Length..].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(TriSquareEdit.Parse).ToList();
                if (!game.TryApplyEdits(edits, out var error))
                {
                    throw new FormatException($"Stored dice turn '{token}' is invalid: {error}");
                }
            }
            else if (!game.TryPlay(token, out _, out var error))
            {
                throw new FormatException($"Stored move '{token}' is invalid: {error}");
            }
        }

        return game;
    }

    public bool TryPlay(string input, [NotNullWhen(true)] out TriTurn? played, [NotNullWhen(false)] out string? error)
    {
        played = null;
        if (IsOver)
        {
            error = "The game is already over.";
            return false;
        }

        if (!TriNotation.TryParseMove(Current, input, out var move, out error))
        {
            return false;
        }

        var mover = Current.SideToMove;
        played = new TriTurn(mover, move, TriNotation.ToSan(Current, move));
        _moves.Add(played);
        _tokens.Add(move.ToToken());
        Current = Current.Apply(move);
        AdvanceFrom(mover);
        return true;
    }

    /// <summary>
    /// Checks whether board edits may replace the side to move's move: kings can't be touched or created, pawns
    /// can't sit on an opponent's back rank (where they would already have promoted), the side to move can't leave
    /// itself in check, and every opponent must still have a legal move (dice can't checkmate or stalemate anyone).
    /// No edits at all is fine: the turn just passes.
    /// </summary>
    public static bool ValidateEdits(TriPosition position, IReadOnlyList<TriSquareEdit> edits, [NotNullWhen(false)] out string? error)
    {
        foreach (var edit in edits)
        {
            if (position[edit.Square].Type == PieceType.King || edit.Piece.Type == PieceType.King)
            {
                error = "Kings can't be changed by dice.";
                return false;
            }

            if (edit.Piece.Type == PieceType.Pawn && TriSquare.Half(edit.Square) != edit.Piece.Color && TriSquare.Rank(edit.Square) == 0)
            {
                error = "Pawns can't be placed on an opponent's back rank, where they would promote.";
                return false;
            }
        }

        var after = position.WithEdits(edits);
        if (after.IsInCheck(position.SideToMove))
        {
            error = "That would leave your own king in check.";
            return false;
        }

        foreach (var opponent in after.AliveColors.Where(c => c != position.SideToMove))
        {
            var theirTurn = after.WithSideToMove(opponent);
            if (theirTurn.GenerateLegalMoves().Count == 0)
            {
                error = theirTurn.IsInCheck() ? "Dice can't checkmate anyone." : "That would leave an opponent with no legal move.";
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>Applies dice edits in place of the side to move's move, then hands the turn on.</summary>
    public bool TryApplyEdits(IReadOnlyList<TriSquareEdit> edits, [NotNullWhen(false)] out string? error)
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

        var mover = Current.SideToMove;
        _moves.Add(new TriTurn(mover, default, "{" + string.Join(' ', edits.Select(e => e.ToString()).Prepend("dice")) + "}", edits));
        _tokens.Add(DicePrefix + string.Join(',', edits));
        Current = Current.WithEdits(edits);
        AdvanceFrom(mover);
        return true;
    }

    /// <summary>A side may resign at any time, not only on its turn. Its pieces leave the board.</summary>
    public void Resign(TriColor color)
    {
        if (IsOver || !Current.IsAlive(color))
        {
            return;
        }

        _tokens.Add(ResignPrefix + TriColors.Letter(color));
        Eliminate(color, Termination.Resignation, null);
        if (!IsOver && Current.SideToMove == color)
        {
            AdvanceFrom(color);
        }
    }

    /// <summary>Everyone still playing agrees to a draw: they share first place.</summary>
    public void AgreeDraw()
    {
        if (!IsOver)
        {
            _tokens.Add(DrawToken);
            EndInDraw(Termination.DrawAgreement);
        }
    }

    public void Abandon()
    {
        if (!IsOver)
        {
            _tokens.Add(AbortToken);
            IsOver = true;
            Termination = Termination.Abandoned;
        }
    }

    /// <summary>Move list grouped into rounds, with eliminations noted, e.g. "1. d4 i5 e10 2. ...".</summary>
    public string MovesText()
    {
        var sb = new StringBuilder();
        var round = 0;
        TriColor? previous = null;
        foreach (var turn in _moves)
        {
            if (previous is null || turn.Player <= previous)
            {
                sb.Append(sb.Length > 0 ? " " : "").Append(++round).Append('.');
            }

            sb.Append(' ').Append(turn.San);
            previous = turn.Player;
        }

        foreach (var e in _eliminations)
        {
            sb.Append($" {{{e.Player.Name()} {(e.Reason == Termination.Checkmate ? "checkmated" : "resigned")}}}");
        }

        return sb.ToString();
    }

    private void Eliminate(TriColor color, Termination reason, TriColor? by)
    {
        _eliminations.Add(new TriElimination(color, reason, by));
        Current = Current.Without(color);
        Termination = reason;
        if (Current.AliveColors.Count() == 1)
        {
            IsOver = true;
        }
        else if (Current.OnlyKingsLeft)
        {
            EndInDraw(Termination.InsufficientMaterial);
        }
    }

    /// <summary>Hands the turn on after <paramref name="mover"/>, knocking out anyone checkmated on the way.</summary>
    private void AdvanceFrom(TriColor mover)
    {
        if (IsOver)
        {
            return;
        }

        if (Current.OnlyKingsLeft)
        {
            EndInDraw(Termination.InsufficientMaterial);
            return;
        }

        var side = mover;
        var passes = 0;
        while (!IsOver)
        {
            side = NextAlive(side);
            Current = Current.WithSideToMove(side);
            if (Current.GenerateLegalMoves().Count > 0)
            {
                return;
            }

            if (Current.IsInCheck(side))
            {
                Eliminate(side, Termination.Checkmate, mover);
                continue;
            }

            // Stalemate: with three left the side passes; with two it's a draw, as in normal chess.
            if (Current.AliveColors.Count() == 2 || ++passes >= Current.AliveColors.Count())
            {
                EndInDraw(Termination.Stalemate);
            }
        }
    }

    private TriColor NextAlive(TriColor from)
    {
        var next = from.Next();
        while (!Current.IsAlive(next))
        {
            next = next.Next();
        }

        return next;
    }

    private void EndInDraw(Termination reason)
    {
        _drawnBetween.AddRange(Current.AliveColors);
        IsOver = true;
        Termination = reason;
    }
}
