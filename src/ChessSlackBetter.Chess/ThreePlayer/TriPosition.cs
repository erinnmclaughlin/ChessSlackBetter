using System.Text;

namespace ChessSlackBetter.Chess.ThreePlayer;

/// <summary>A move on the three-player board. Square names have one or two digits, e.g. "d2d4" or "i7i5".</summary>
public readonly record struct TriMove(int From, int To, PieceType Promotion = PieceType.None)
{
    public string ToToken()
    {
        var promotion = Promotion == PieceType.None ? "" : Promotion.SanLetter().ToLowerInvariant();
        return TriSquare.Name(From) + TriSquare.Name(To) + promotion;
    }

    public override string ToString() => ToToken();
}

/// <summary>
/// An immutable three-player position. A side whose king is gone has been eliminated and has no pieces left.
/// Kings are never captured: a king in check stays on the board until its owner's turn, when it must get out of
/// check or be checkmated. There is no en passant.
/// </summary>
public sealed class TriPosition
{
    private static readonly (int Df, int Dr)[] Orthogonal = [(1, 0), (-1, 0), (0, 1), (0, -1)];
    private static readonly (int Df, int Dr)[] Diagonal = [(1, 1), (-1, 1), (1, -1), (-1, -1)];
    private static readonly (int Df, int Dr)[] AllDirections = [.. Orthogonal, .. Diagonal];
    private static readonly PieceType[] BackRank =
        [PieceType.Rook, PieceType.Knight, PieceType.Bishop, PieceType.Queen, PieceType.King, PieceType.Bishop, PieceType.Knight, PieceType.Rook];

    private static readonly PieceType[] Promotions = [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];

    private readonly TriPiece[] _board;

    private TriPosition(TriPiece[] board, TriColor sideToMove, int castling)
    {
        _board = board;
        SideToMove = sideToMove;
        Castling = castling;
    }

    /// <summary>Every side sets up the same way in its own frame: R N B Q K B N R, with pawns in front.</summary>
    public static TriPosition Start { get; } = BuildStart();

    public TriColor SideToMove { get; }

    /// <summary>Castling rights: bit 2c is side c's kingside (file 7 rook), bit 2c+1 its queenside (file 0 rook).</summary>
    public int Castling { get; }

    public TriPiece this[int square] => _board[square];

    public bool IsAlive(TriColor color) => FindKing(color) >= 0;

    public IEnumerable<TriColor> AliveColors => TriColors.All.Where(IsAlive);

    public int FindKing(TriColor color) => Array.IndexOf(_board, new TriPiece(PieceType.King, color));

    public bool IsInCheck(TriColor color)
    {
        var king = FindKing(color);
        return king >= 0 && TriColors.All.Any(c => c != color && Attacks(c)[king]);
    }

    public bool IsInCheck() => IsInCheck(SideToMove);

    /// <summary>The squares a side attacks, whatever stands on them.</summary>
    public bool[] Attacks(TriColor color)
    {
        var attacked = new bool[TriSquare.Count];
        for (var sq = 0; sq < TriSquare.Count; sq++)
        {
            if (_board[sq] is { IsEmpty: false } piece && piece.Color == color)
            {
                foreach (var target in Targets(sq, piece))
                {
                    attacked[target] = true;
                }
            }
        }

        return attacked;
    }

    public IReadOnlyList<TriMove> GenerateLegalMoves() =>
        GeneratePseudoLegalMoves(SideToMove).Where(m => !Apply(m).IsInCheck(SideToMove)).ToList();

    /// <summary>Plays a move for the side to move without checking legality. The turn does not change.</summary>
    public TriPosition Apply(TriMove move)
    {
        var board = (TriPiece[])_board.Clone();
        var piece = board[move.From];
        board[move.To] = move.Promotion == PieceType.None ? piece : new TriPiece(move.Promotion, piece.Color);
        board[move.From] = TriPiece.Empty;

        if (IsCastle(move, piece))
        {
            var kingSide = TriSquare.File(move.To) == 6;
            var half = TriSquare.Half(move.From);
            board[TriSquare.At(half, kingSide ? 5 : 3, 0)] = board[TriSquare.At(half, kingSide ? 7 : 0, 0)];
            board[TriSquare.At(half, kingSide ? 7 : 0, 0)] = TriPiece.Empty;
        }

        var castling = Castling;
        foreach (var color in TriColors.All)
        {
            var king = TriSquare.At(color, 4, 0);
            var kingRook = TriSquare.At(color, 7, 0);
            var queenRook = TriSquare.At(color, 0, 0);
            if (move.From == king || move.From == kingRook || move.To == kingRook)
            {
                castling &= ~(1 << (2 * (int)color));
            }

            if (move.From == king || move.From == queenRook || move.To == queenRook)
            {
                castling &= ~(1 << (2 * (int)color + 1));
            }
        }

        return new TriPosition(board, SideToMove, castling);
    }

    public TriPosition WithSideToMove(TriColor color) => new(_board, color, Castling);

    /// <summary>
    /// Changes pieces on the board in place of a move. The turn does not change; castling rights are dropped for
    /// any king or rook no longer on its home square. Does not check legality.
    /// </summary>
    public TriPosition WithEdits(IEnumerable<TriSquareEdit> edits)
    {
        var board = (TriPiece[])_board.Clone();
        foreach (var edit in edits)
        {
            board[edit.Square] = edit.Piece;
        }

        var castling = Castling;
        foreach (var color in TriColors.All)
        {
            var kingHome = board[TriSquare.At(color, 4, 0)] == new TriPiece(PieceType.King, color);
            for (var side = 0; side < 2; side++)
            {
                var rook = TriSquare.At(color, side == 0 ? 7 : 0, 0);
                if (!kingHome || board[rook] != new TriPiece(PieceType.Rook, color))
                {
                    castling &= ~(1 << (2 * (int)color + side));
                }
            }
        }

        return new TriPosition(board, SideToMove, castling);
    }

    /// <summary>Takes an eliminated side's pieces off the board.</summary>
    public TriPosition Without(TriColor color)
    {
        var board = _board.Select(p => !p.IsEmpty && p.Color == color ? TriPiece.Empty : p).ToArray();
        return new TriPosition(board, SideToMove, Castling & ~(3 << (2 * (int)color)));
    }

    /// <summary>True when the only pieces left are kings.</summary>
    public bool OnlyKingsLeft => _board.All(p => p.IsEmpty || p.Type == PieceType.King);

    /// <summary>Compact text for image URLs: side to move, castling rights, then the squares in index order.</summary>
    public string ToCode()
    {
        var sb = new StringBuilder();
        sb.Append(TriColors.Letter(SideToMove)).Append('_').Append(Castling).Append('_');
        var empty = 0;
        foreach (var piece in _board)
        {
            if (piece.IsEmpty)
            {
                empty++;
                continue;
            }

            if (empty > 0)
            {
                sb.Append(empty);
                empty = 0;
            }

            sb.Append(piece.ToCode());
        }

        if (empty > 0)
        {
            sb.Append(empty);
        }

        return sb.ToString();
    }

    public static TriPosition FromCode(string code)
    {
        var parts = code.Split('_');
        if (parts.Length != 3 || parts[0].Length != 1 || TriColors.FromLetter(parts[0][0]) is not { } side ||
            !int.TryParse(parts[1], out var castling))
        {
            throw new FormatException($"Invalid position code '{code}'.");
        }

        var board = new TriPiece[TriSquare.Count];
        var sq = 0;
        var text = parts[2];
        for (var i = 0; i < text.Length;)
        {
            if (char.IsAsciiDigit(text[i]))
            {
                var start = i;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                sq += int.Parse(text.AsSpan(start, i - start));
            }
            else if (i + 1 < text.Length && sq < TriSquare.Count)
            {
                board[sq++] = TriPiece.FromCode(text.AsSpan(i, 2));
                i += 2;
            }
            else
            {
                throw new FormatException($"Invalid position code '{code}'.");
            }
        }

        if (sq != TriSquare.Count)
        {
            throw new FormatException($"Position code '{code}' doesn't cover all {TriSquare.Count} squares.");
        }

        return new TriPosition(board, side, castling);
    }

    /// <summary>
    /// Squares a piece reaches: for sliders, knights and kings every square up to and including the first piece in
    /// the way; for pawns, the two diagonal capture squares (pushes are handled separately).
    /// </summary>
    private IEnumerable<int> Targets(int square, TriPiece piece)
    {
        switch (piece.Type)
        {
            case PieceType.Pawn:
                var forward = PawnForward(square, piece.Color);
                foreach (var df in new[] { -1, 1 })
                {
                    foreach (var (target, _) in TriSquare.Step(square, df, forward))
                    {
                        yield return target;
                    }
                }

                break;
            case PieceType.Knight:
                foreach (var target in KnightTargets(square))
                {
                    yield return target;
                }

                break;
            case PieceType.King:
                foreach (var (df, dr) in AllDirections)
                {
                    foreach (var (target, _) in TriSquare.Step(square, df, dr))
                    {
                        yield return target;
                    }
                }

                break;
            default:
                var directions = piece.Type switch
                {
                    PieceType.Rook => Orthogonal,
                    PieceType.Bishop => Diagonal,
                    _ => AllDirections,
                };
                foreach (var (df, dr) in directions)
                {
                    foreach (var target in Ray(square, df, dr))
                    {
                        yield return target;
                    }
                }

                break;
        }
    }

    /// <summary>Slides until blocked. A ray through the centre splits in two, and both branches carry on.</summary>
    private IEnumerable<int> Ray(int square, int df, int dr)
    {
        var frontier = new List<(int Square, int Df, int Dr)> { (square, df, dr) };
        while (frontier.Count > 0)
        {
            var next = new List<(int, int, int)>();
            foreach (var (from, fdf, fdr) in frontier)
            {
                foreach (var (target, flipped) in TriSquare.Step(from, fdf, fdr))
                {
                    yield return target;
                    if (_board[target].IsEmpty)
                    {
                        next.Add(flipped ? (target, -fdf, -fdr) : (target, fdf, fdr));
                    }
                }
            }

            frontier = next;
        }
    }

    /// <summary>
    /// A knight goes two squares one way and one square at right angles, in either order. On a normal board both
    /// orders land on the same square; near the middle they can differ, and both count.
    /// </summary>
    private static IEnumerable<int> KnightTargets(int square)
    {
        var targets = new HashSet<int>();
        foreach (var (a, b) in new[] { (1, 2), (2, 1) })
        {
            foreach (var sf in new[] { -1, 1 })
            {
                foreach (var sr in new[] { -1, 1 })
                {
                    var df = a * sf;
                    var dr = b * sr;
                    var fileSteps = Enumerable.Repeat((Math.Sign(df), 0), Math.Abs(df));
                    var rankSteps = Enumerable.Repeat((0, Math.Sign(dr)), Math.Abs(dr));
                    AddPath(square, fileSteps.Concat(rankSteps), targets);
                    AddPath(square, rankSteps.Concat(fileSteps), targets);
                }
            }
        }

        return targets;
    }

    private static void AddPath(int square, IEnumerable<(int Df, int Dr)> steps, HashSet<int> targets)
    {
        var current = square;
        var flip = 1;
        foreach (var (df, dr) in steps)
        {
            // Orthogonal steps never branch.
            var next = TriSquare.Step(current, df * flip, dr * flip).ToList();
            if (next.Count == 0)
            {
                return;
            }

            current = next[0].Square;
            flip = next[0].Flipped ? -flip : flip;
        }

        targets.Add(current);
    }

    private IEnumerable<TriMove> GeneratePseudoLegalMoves(TriColor color)
    {
        for (var sq = 0; sq < TriSquare.Count; sq++)
        {
            var piece = _board[sq];
            if (piece.IsEmpty || piece.Color != color)
            {
                continue;
            }

            if (piece.Type == PieceType.Pawn)
            {
                foreach (var move in PawnMoves(sq, color))
                {
                    yield return move;
                }

                continue;
            }

            foreach (var target in Targets(sq, piece))
            {
                if (CanLandOn(target, color))
                {
                    yield return new TriMove(sq, target);
                }
            }

            if (piece.Type == PieceType.King)
            {
                foreach (var castle in CastlingMoves(sq, color))
                {
                    yield return castle;
                }
            }
        }
    }

    private IEnumerable<TriMove> PawnMoves(int square, TriColor color)
    {
        var forward = PawnForward(square, color);
        foreach (var (one, _) in TriSquare.Step(square, 0, forward))
        {
            if (!_board[one].IsEmpty)
            {
                break;
            }

            foreach (var move in WithPromotions(square, one, color))
            {
                yield return move;
            }

            if (TriSquare.Half(square) == color && TriSquare.Rank(square) == 1)
            {
                var two = TriSquare.At(color, TriSquare.File(square), 3);
                if (_board[two].IsEmpty)
                {
                    yield return new TriMove(square, two);
                }
            }
        }

        foreach (var target in Targets(square, _board[square]))
        {
            if (_board[target] is { IsEmpty: false } victim && victim.Color != color && victim.Type != PieceType.King)
            {
                foreach (var move in WithPromotions(square, target, color))
                {
                    yield return move;
                }
            }
        }
    }

    /// <summary>A pawn promotes on reaching either opponent's back rank.</summary>
    private static IEnumerable<TriMove> WithPromotions(int from, int to, TriColor color)
    {
        if (TriSquare.Half(to) != color && TriSquare.Rank(to) == 0)
        {
            return Promotions.Select(p => new TriMove(from, to, p));
        }

        return [new TriMove(from, to)];
    }

    /// <summary>Pawns head for the middle in their own half, then on to the back rank of whichever half they enter.</summary>
    private static int PawnForward(int square, TriColor color) => TriSquare.Half(square) == color ? 1 : -1;

    private bool CanLandOn(int target, TriColor color) =>
        _board[target] is var p && (p.IsEmpty || (p.Color != color && p.Type != PieceType.King));

    private IEnumerable<TriMove> CastlingMoves(int king, TriColor color)
    {
        if (king != TriSquare.At(color, 4, 0) || IsInCheck(color))
        {
            yield break;
        }

        foreach (var kingSide in new[] { true, false })
        {
            if ((Castling & (1 << (2 * (int)color + (kingSide ? 0 : 1)))) == 0 ||
                _board[TriSquare.At(color, kingSide ? 7 : 0, 0)] != new TriPiece(PieceType.Rook, color))
            {
                continue;
            }

            int[] between = kingSide ? [5, 6] : [1, 2, 3];
            int[] kingPath = kingSide ? [5, 6] : [3, 2];
            if (between.Any(f => !_board[TriSquare.At(color, f, 0)].IsEmpty))
            {
                continue;
            }

            var attacked = TriColors.All.Where(c => c != color).Select(Attacks).ToList();
            if (kingPath.Any(f => attacked.Any(a => a[TriSquare.At(color, f, 0)])))
            {
                continue;
            }

            yield return new TriMove(king, TriSquare.At(color, kingSide ? 6 : 2, 0));
        }
    }

    private static bool IsCastle(TriMove move, TriPiece piece) =>
        piece.Type == PieceType.King &&
        move.From == TriSquare.At(piece.Color, 4, 0) &&
        TriSquare.Half(move.To) == piece.Color &&
        TriSquare.Rank(move.To) == 0 &&
        Math.Abs(TriSquare.File(move.To) - 4) == 2;

    private static TriPosition BuildStart()
    {
        var board = new TriPiece[TriSquare.Count];
        foreach (var color in TriColors.All)
        {
            for (var file = 0; file < 8; file++)
            {
                board[TriSquare.At(color, file, 0)] = new TriPiece(BackRank[file], color);
                board[TriSquare.At(color, file, 1)] = new TriPiece(PieceType.Pawn, color);
            }
        }

        return new TriPosition(board, TriColor.White, 0b111111);
    }
}
