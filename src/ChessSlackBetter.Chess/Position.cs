using System.Text;

namespace ChessSlackBetter.Chess;

/// <summary>
/// An immutable chess position. <see cref="Apply"/> returns a new position rather than mutating this one.
/// </summary>
public sealed class Position
{
    public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly (int File, int Rank)[] KnightOffsets =
        [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];

    private static readonly (int File, int Rank)[] KingOffsets =
        [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    private static readonly (int File, int Rank)[] RookDirections = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private static readonly (int File, int Rank)[] BishopDirections = [(1, 1), (1, -1), (-1, 1), (-1, -1)];

    private static readonly PieceType[] PromotionPieces =
        [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight];

    private readonly Piece[] _board;

    private Position(
        Piece[] board,
        PieceColor sideToMove,
        CastlingRights castling,
        int enPassantSquare,
        int halfmoveClock,
        int fullmoveNumber)
    {
        _board = board;
        SideToMove = sideToMove;
        Castling = castling;
        EnPassantSquare = enPassantSquare;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
    }

    public static Position Start { get; } = FromFen(StartFen);

    public PieceColor SideToMove { get; }

    public CastlingRights Castling { get; }

    /// <summary>The square a pawn may capture onto en passant, or -1.</summary>
    public int EnPassantSquare { get; }

    public int HalfmoveClock { get; }

    public int FullmoveNumber { get; }

    public Piece this[int square] => _board[square];

    public static Position FromFen(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4)
        {
            throw new FormatException("FEN must have at least 4 fields.");
        }

        var board = new Piece[64];
        var ranks = parts[0].Split('/');
        if (ranks.Length != 8)
        {
            throw new FormatException("FEN board must have 8 ranks.");
        }

        for (var i = 0; i < 8; i++)
        {
            var rank = 7 - i;
            var file = 0;
            foreach (var c in ranks[i])
            {
                if (char.IsDigit(c))
                {
                    file += c - '0';
                }
                else
                {
                    if (file > 7)
                    {
                        throw new FormatException($"Too many squares in FEN rank {rank + 1}.");
                    }

                    board[Square.At(file, rank)] = Piece.FromFenChar(c);
                    file++;
                }
            }

            if (file != 8)
            {
                throw new FormatException($"FEN rank {rank + 1} does not have 8 squares.");
            }
        }

        var side = parts[1] switch
        {
            "w" => PieceColor.White,
            "b" => PieceColor.Black,
            _ => throw new FormatException("FEN side to move must be 'w' or 'b'."),
        };

        var castling = CastlingRights.None;
        if (parts[2] != "-")
        {
            foreach (var c in parts[2])
            {
                castling |= c switch
                {
                    'K' => CastlingRights.WhiteKingSide,
                    'Q' => CastlingRights.WhiteQueenSide,
                    'k' => CastlingRights.BlackKingSide,
                    'q' => CastlingRights.BlackQueenSide,
                    _ => throw new FormatException($"Invalid castling character '{c}'."),
                };
            }
        }

        var enPassant = parts[3] == "-" ? -1 : Square.Parse(parts[3]);
        var halfmove = parts.Length > 4 ? int.Parse(parts[4]) : 0;
        var fullmove = parts.Length > 5 ? int.Parse(parts[5]) : 1;

        var position = new Position(board, side, castling, enPassant, halfmove, fullmove);
        if (position.FindKing(PieceColor.White) < 0 || position.FindKing(PieceColor.Black) < 0)
        {
            throw new FormatException("Both sides must have a king.");
        }

        return position;
    }

    public string ToFen()
    {
        var sb = new StringBuilder();
        sb.Append(BoardFen());
        sb.Append(SideToMove == PieceColor.White ? " w " : " b ");
        sb.Append(CastlingFen());
        sb.Append(' ');
        sb.Append(EnPassantSquare < 0 ? "-" : Square.Name(EnPassantSquare));
        sb.Append(' ').Append(HalfmoveClock).Append(' ').Append(FullmoveNumber);
        return sb.ToString();
    }

    /// <summary>
    /// Identifies a position for repetition purposes: placement, side to move, castling rights and
    /// en passant square (only when an en passant capture is actually possible).
    /// </summary>
    public string RepetitionKey()
    {
        var ep = EnPassantSquare >= 0 && GenerateLegalMoves().Any(m => m.To == EnPassantSquare && _board[m.From].Type == PieceType.Pawn)
            ? Square.Name(EnPassantSquare)
            : "-";
        return $"{BoardFen()} {(SideToMove == PieceColor.White ? 'w' : 'b')} {CastlingFen()} {ep}";
    }

    public int FindKing(PieceColor color)
    {
        for (var sq = 0; sq < 64; sq++)
        {
            if (_board[sq] == new Piece(PieceType.King, color))
            {
                return sq;
            }
        }

        return -1;
    }

    public bool IsInCheck() => IsInCheck(SideToMove);

    public bool IsInCheck(PieceColor color) => IsSquareAttacked(FindKing(color), color.Opponent());

    public bool IsSquareAttacked(int square, PieceColor byColor)
    {
        var file = Square.File(square);
        var rank = Square.Rank(square);

        // Pawns attack diagonally forward, so look one rank "behind" the target from the attacker's view.
        var pawnRank = byColor == PieceColor.White ? rank - 1 : rank + 1;
        foreach (var df in (int[])[-1, 1])
        {
            if (Square.IsOnBoard(file + df, pawnRank) &&
                _board[Square.At(file + df, pawnRank)] == new Piece(PieceType.Pawn, byColor))
            {
                return true;
            }
        }

        if (AttackedByStep(file, rank, KnightOffsets, new Piece(PieceType.Knight, byColor)) ||
            AttackedByStep(file, rank, KingOffsets, new Piece(PieceType.King, byColor)))
        {
            return true;
        }

        return AttackedBySlider(file, rank, RookDirections, byColor, PieceType.Rook) ||
               AttackedBySlider(file, rank, BishopDirections, byColor, PieceType.Bishop);
    }

    public IReadOnlyList<Move> GenerateLegalMoves()
    {
        var legal = new List<Move>();
        foreach (var move in GeneratePseudoLegalMoves())
        {
            if (!Apply(move).IsInCheck(SideToMove))
            {
                legal.Add(move);
            }
        }

        return legal;
    }

    public bool IsLegal(Move move) => GenerateLegalMoves().Contains(move);

    /// <summary>Plays a move without checking legality. Use <see cref="IsLegal"/> first for untrusted input.</summary>
    public Position Apply(Move move)
    {
        var board = (Piece[])_board.Clone();
        var piece = board[move.From];
        var captured = board[move.To];
        var isPawn = piece.Type == PieceType.Pawn;
        var fromFile = Square.File(move.From);
        var toFile = Square.File(move.To);

        board[move.To] = move.Promotion == PieceType.None ? piece : new Piece(move.Promotion, piece.Color);
        board[move.From] = Piece.Empty;

        var isEnPassant = isPawn && move.To == EnPassantSquare && fromFile != toFile && captured.IsEmpty;
        if (isEnPassant)
        {
            board[Square.At(toFile, Square.Rank(move.From))] = Piece.Empty;
        }

        if (piece.Type == PieceType.King && Math.Abs(toFile - fromFile) == 2)
        {
            var rank = Square.Rank(move.From);
            var (rookFrom, rookTo) = toFile == 6 ? (Square.At(7, rank), Square.At(5, rank)) : (Square.At(0, rank), Square.At(3, rank));
            board[rookTo] = board[rookFrom];
            board[rookFrom] = Piece.Empty;
        }

        var castling = Castling & ~(CastlingLostBy(move.From) | CastlingLostBy(move.To));

        var enPassant = -1;
        if (isPawn && Math.Abs(Square.Rank(move.To) - Square.Rank(move.From)) == 2)
        {
            enPassant = (move.From + move.To) / 2;
        }

        var resetsClock = isPawn || !captured.IsEmpty;
        return new Position(
            board,
            SideToMove.Opponent(),
            castling,
            enPassant,
            resetsClock ? 0 : HalfmoveClock + 1,
            SideToMove == PieceColor.Black ? FullmoveNumber + 1 : FullmoveNumber);
    }

    /// <summary>True when neither side can possibly deliver mate (K v K, K+minor v K, K+B v K+B on same colour).</summary>
    public bool IsInsufficientMaterial()
    {
        var minors = new List<(Piece Piece, int Square)>();
        for (var sq = 0; sq < 64; sq++)
        {
            var p = _board[sq];
            switch (p.Type)
            {
                case PieceType.None or PieceType.King:
                    continue;
                case PieceType.Knight or PieceType.Bishop:
                    minors.Add((p, sq));
                    break;
                default:
                    return false;
            }
        }

        if (minors.Count <= 1)
        {
            return true;
        }

        // Any number of bishops, all on the same square colour, cannot mate.
        return minors.All(m => m.Piece.Type == PieceType.Bishop) &&
               minors.Select(m => Square.IsLight(m.Square)).Distinct().Count() == 1;
    }

    private static CastlingRights CastlingLostBy(int square) => square switch
    {
        4 => CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide,
        0 => CastlingRights.WhiteQueenSide,
        7 => CastlingRights.WhiteKingSide,
        60 => CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide,
        56 => CastlingRights.BlackQueenSide,
        63 => CastlingRights.BlackKingSide,
        _ => CastlingRights.None,
    };

    private string BoardFen()
    {
        var sb = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var p = _board[Square.At(file, rank)];
                if (p.IsEmpty)
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }

                sb.Append(p.ToFenChar());
            }

            if (empty > 0)
            {
                sb.Append(empty);
            }

            if (rank > 0)
            {
                sb.Append('/');
            }
        }

        return sb.ToString();
    }

    private string CastlingFen()
    {
        if (Castling == CastlingRights.None)
        {
            return "-";
        }

        var sb = new StringBuilder();
        if (Castling.HasFlag(CastlingRights.WhiteKingSide)) sb.Append('K');
        if (Castling.HasFlag(CastlingRights.WhiteQueenSide)) sb.Append('Q');
        if (Castling.HasFlag(CastlingRights.BlackKingSide)) sb.Append('k');
        if (Castling.HasFlag(CastlingRights.BlackQueenSide)) sb.Append('q');
        return sb.ToString();
    }

    private bool AttackedByStep(int file, int rank, (int File, int Rank)[] offsets, Piece attacker)
    {
        foreach (var (df, dr) in offsets)
        {
            if (Square.IsOnBoard(file + df, rank + dr) && _board[Square.At(file + df, rank + dr)] == attacker)
            {
                return true;
            }
        }

        return false;
    }

    private bool AttackedBySlider(int file, int rank, (int File, int Rank)[] directions, PieceColor byColor, PieceType slider)
    {
        foreach (var (df, dr) in directions)
        {
            var f = file + df;
            var r = rank + dr;
            while (Square.IsOnBoard(f, r))
            {
                var p = _board[Square.At(f, r)];
                if (!p.IsEmpty)
                {
                    if (p.Color == byColor && (p.Type == slider || p.Type == PieceType.Queen))
                    {
                        return true;
                    }

                    break;
                }

                f += df;
                r += dr;
            }
        }

        return false;
    }

    private IEnumerable<Move> GeneratePseudoLegalMoves()
    {
        var moves = new List<Move>();
        for (var sq = 0; sq < 64; sq++)
        {
            var p = _board[sq];
            if (p.IsEmpty || p.Color != SideToMove)
            {
                continue;
            }

            switch (p.Type)
            {
                case PieceType.Pawn:
                    AddPawnMoves(sq, moves);
                    break;
                case PieceType.Knight:
                    AddStepMoves(sq, KnightOffsets, moves);
                    break;
                case PieceType.Bishop:
                    AddSlideMoves(sq, BishopDirections, moves);
                    break;
                case PieceType.Rook:
                    AddSlideMoves(sq, RookDirections, moves);
                    break;
                case PieceType.Queen:
                    AddSlideMoves(sq, RookDirections, moves);
                    AddSlideMoves(sq, BishopDirections, moves);
                    break;
                case PieceType.King:
                    AddStepMoves(sq, KingOffsets, moves);
                    AddCastlingMoves(sq, moves);
                    break;
            }
        }

        return moves;
    }

    private void AddPawnMoves(int from, List<Move> moves)
    {
        var file = Square.File(from);
        var rank = Square.Rank(from);
        var dir = SideToMove == PieceColor.White ? 1 : -1;
        var startRank = SideToMove == PieceColor.White ? 1 : 6;
        var promotionRank = SideToMove == PieceColor.White ? 7 : 0;

        void AddPawnMove(int to)
        {
            if (Square.Rank(to) == promotionRank)
            {
                foreach (var promo in PromotionPieces)
                {
                    moves.Add(new Move(from, to, promo));
                }
            }
            else
            {
                moves.Add(new Move(from, to));
            }
        }

        var oneAhead = Square.At(file, rank + dir);
        if (_board[oneAhead].IsEmpty)
        {
            AddPawnMove(oneAhead);
            if (rank == startRank && _board[Square.At(file, rank + 2 * dir)].IsEmpty)
            {
                moves.Add(new Move(from, Square.At(file, rank + 2 * dir)));
            }
        }

        foreach (var df in (int[])[-1, 1])
        {
            if (!Square.IsOnBoard(file + df, rank + dir))
            {
                continue;
            }

            var to = Square.At(file + df, rank + dir);
            var target = _board[to];
            if ((!target.IsEmpty && target.Color != SideToMove) || to == EnPassantSquare)
            {
                AddPawnMove(to);
            }
        }
    }

    private void AddStepMoves(int from, (int File, int Rank)[] offsets, List<Move> moves)
    {
        var file = Square.File(from);
        var rank = Square.Rank(from);
        foreach (var (df, dr) in offsets)
        {
            if (!Square.IsOnBoard(file + df, rank + dr))
            {
                continue;
            }

            var to = Square.At(file + df, rank + dr);
            var target = _board[to];
            if (target.IsEmpty || target.Color != SideToMove)
            {
                moves.Add(new Move(from, to));
            }
        }
    }

    private void AddSlideMoves(int from, (int File, int Rank)[] directions, List<Move> moves)
    {
        foreach (var (df, dr) in directions)
        {
            var f = Square.File(from) + df;
            var r = Square.Rank(from) + dr;
            while (Square.IsOnBoard(f, r))
            {
                var to = Square.At(f, r);
                var target = _board[to];
                if (target.IsEmpty)
                {
                    moves.Add(new Move(from, to));
                }
                else
                {
                    if (target.Color != SideToMove)
                    {
                        moves.Add(new Move(from, to));
                    }

                    break;
                }

                f += df;
                r += dr;
            }
        }
    }

    private void AddCastlingMoves(int kingSquare, List<Move> moves)
    {
        var white = SideToMove == PieceColor.White;
        var homeRank = white ? 0 : 7;
        if (kingSquare != Square.At(4, homeRank))
        {
            return;
        }

        var opponent = SideToMove.Opponent();
        var kingSide = white ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        var queenSide = white ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        var rook = new Piece(PieceType.Rook, SideToMove);

        if (Castling.HasFlag(kingSide) &&
            _board[Square.At(7, homeRank)] == rook &&
            _board[Square.At(5, homeRank)].IsEmpty &&
            _board[Square.At(6, homeRank)].IsEmpty &&
            !IsSquareAttacked(kingSquare, opponent) &&
            !IsSquareAttacked(Square.At(5, homeRank), opponent))
        {
            moves.Add(new Move(kingSquare, Square.At(6, homeRank)));
        }

        if (Castling.HasFlag(queenSide) &&
            _board[Square.At(0, homeRank)] == rook &&
            _board[Square.At(1, homeRank)].IsEmpty &&
            _board[Square.At(2, homeRank)].IsEmpty &&
            _board[Square.At(3, homeRank)].IsEmpty &&
            !IsSquareAttacked(kingSquare, opponent) &&
            !IsSquareAttacked(Square.At(3, homeRank), opponent))
        {
            moves.Add(new Move(kingSquare, Square.At(2, homeRank)));
        }
    }
}
