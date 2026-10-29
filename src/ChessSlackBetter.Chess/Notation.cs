using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace ChessSlackBetter.Chess;

public static partial class Notation
{
    [GeneratedRegex(@"^([a-h][1-8])[-x]?([a-h][1-8])=?([qrbn])?$", RegexOptions.IgnoreCase)]
    private static partial Regex LongAlgebraic();

    [GeneratedRegex(@"^([NBRQK])?([a-h])?([1-8])?x?([a-h][1-8])(?:=?([NBRQ]))?$")]
    private static partial Regex StandardAlgebraic();

    [GeneratedRegex(@"^[O0]-?[O0](-?[O0])?$", RegexOptions.IgnoreCase)]
    private static partial Regex Castle();

    /// <summary>True if the text has the shape of a move (it may still be illegal).</summary>
    public static bool LooksLikeMove(string text)
    {
        var s = Normalize(text);
        return s.Length is >= 2 and <= 8 &&
               (LongAlgebraic().IsMatch(s) || Castle().IsMatch(s) || StandardAlgebraic().IsMatch(UppercasePieceLetter(s)));
    }

    /// <summary>
    /// Parses a move in long algebraic (e2e4, e7e8q, e2-e4) or standard algebraic notation (Nf3, exd5, O-O, e8=Q+).
    /// Promotions without a piece default to a queen.
    /// </summary>
    public static bool TryParseMove(Position position, string text, out Move move, [NotNullWhen(false)] out string? error)
    {
        move = default;
        error = null;
        var s = Normalize(text);
        var legal = position.GenerateLegalMoves();

        if (Castle().IsMatch(s))
        {
            var kingSide = s.Count(c => c is 'O' or 'o' or '0') == 2;
            var king = position.FindKing(position.SideToMove);
            var to = king + (kingSide ? 2 : -2);
            var castle = legal.Where(m => m.From == king && m.To == to && position[king].Type == PieceType.King).ToList();
            if (castle.Count == 0)
            {
                error = $"You can't castle {(kingSide ? "king" : "queen")}side right now.";
                return false;
            }

            move = castle[0];
            return true;
        }

        var lan = LongAlgebraic().Match(s);
        if (lan.Success)
        {
            var from = Square.Parse(lan.Groups[1].Value);
            var to = Square.Parse(lan.Groups[2].Value);
            var promotion = lan.Groups[3].Success ? PieceTypes.FromLetter(lan.Groups[3].Value[0])!.Value : PieceType.None;
            var candidates = legal.Where(m => m.From == from && m.To == to).ToList();
            return PickPromotion(position, candidates, promotion, s, out move, out error);
        }

        var san = StandardAlgebraic().Match(UppercasePieceLetter(s));
        if (san.Success)
        {
            var pieceType = san.Groups[1].Success ? PieceTypes.FromLetter(san.Groups[1].Value[0])!.Value : PieceType.Pawn;
            var fromFile = san.Groups[2].Success ? san.Groups[2].Value[0] - 'a' : -1;
            var fromRank = san.Groups[3].Success ? san.Groups[3].Value[0] - '1' : -1;
            var to = Square.Parse(san.Groups[4].Value);
            var promotion = san.Groups[5].Success ? PieceTypes.FromLetter(san.Groups[5].Value[0])!.Value : PieceType.None;

            var candidates = legal.Where(m =>
                    m.To == to &&
                    position[m.From].Type == pieceType &&
                    (fromFile < 0 || Square.File(m.From) == fromFile) &&
                    (fromRank < 0 || Square.Rank(m.From) == fromRank))
                .ToList();

            if (candidates.Select(m => m.From).Distinct().Count() > 1)
            {
                var options = string.Join(", ", candidates.Where(m => m.Promotion is PieceType.None or PieceType.Queen).Select(m => ToSan(position, m)));
                error = $"`{text.Trim()}` is ambiguous. Did you mean {options}?";
                return false;
            }

            return PickPromotion(position, candidates, promotion, s, out move, out error);
        }

        error = $"I don't understand the move `{text.Trim()}`. Try something like `e2e4`, `Nf3` or `O-O`.";
        return false;
    }

    public static string ToSan(Position position, Move move)
    {
        var piece = position[move.From];
        var sb = new StringBuilder();

        if (piece.Type == PieceType.King && Math.Abs(Square.File(move.To) - Square.File(move.From)) == 2)
        {
            sb.Append(Square.File(move.To) == 6 ? "O-O" : "O-O-O");
        }
        else
        {
            var isCapture = !position[move.To].IsEmpty ||
                            (piece.Type == PieceType.Pawn && Square.File(move.From) != Square.File(move.To));

            if (piece.Type == PieceType.Pawn)
            {
                if (isCapture)
                {
                    sb.Append((char)('a' + Square.File(move.From)));
                }
            }
            else
            {
                sb.Append(piece.Type.SanLetter());
                sb.Append(Disambiguation(position, move, piece.Type));
            }

            if (isCapture)
            {
                sb.Append('x');
            }

            sb.Append(Square.Name(move.To));
            if (move.Promotion != PieceType.None)
            {
                sb.Append('=').Append(move.Promotion.SanLetter());
            }
        }

        var after = position.Apply(move);
        if (after.IsInCheck())
        {
            sb.Append(after.GenerateLegalMoves().Count == 0 ? '#' : '+');
        }

        return sb.ToString();
    }

    private static string Disambiguation(Position position, Move move, PieceType type)
    {
        var rivals = position.GenerateLegalMoves()
            .Where(m => m.To == move.To && m.From != move.From && position[m.From].Type == type)
            .Select(m => m.From)
            .Distinct()
            .ToList();

        if (rivals.Count == 0)
        {
            return "";
        }

        var file = (char)('a' + Square.File(move.From));
        var rank = (char)('1' + Square.Rank(move.From));
        if (rivals.All(sq => Square.File(sq) != Square.File(move.From)))
        {
            return file.ToString();
        }

        if (rivals.All(sq => Square.Rank(sq) != Square.Rank(move.From)))
        {
            return rank.ToString();
        }

        return $"{file}{rank}";
    }

    private static bool PickPromotion(
        Position position,
        List<Move> candidates,
        PieceType promotion,
        string text,
        out Move move,
        [NotNullWhen(false)] out string? error)
    {
        move = default;
        error = null;
        if (candidates.Count == 0)
        {
            error = $"`{text}` isn't a legal move here.";
            return false;
        }

        var isPromotion = candidates.Any(m => m.Promotion != PieceType.None);
        if (!isPromotion && promotion != PieceType.None)
        {
            error = $"`{text}` isn't a promotion.";
            return false;
        }

        var wanted = isPromotion && promotion == PieceType.None ? PieceType.Queen : promotion;
        var match = candidates.Where(m => m.Promotion == wanted).ToList();
        if (match.Count != 1)
        {
            error = $"`{text}` isn't a legal move here.";
            return false;
        }

        move = match[0];
        return true;
    }

    private static string Normalize(string text) =>
        text.Trim().TrimEnd('+', '#', '!', '?').Replace("×", "x").Replace(":", "x");

    /// <summary>People often type "nf3" or "qxd7"; a lowercase 'b' is ambiguous with the b-file, so it stays a pawn.</summary>
    private static string UppercasePieceLetter(string s) =>
        s.Length > 2 && "nrqk".Contains(s[0]) ? char.ToUpperInvariant(s[0]) + s[1..] : s;
}
