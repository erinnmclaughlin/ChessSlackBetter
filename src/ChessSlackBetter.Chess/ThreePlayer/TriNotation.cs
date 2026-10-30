using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace ChessSlackBetter.Chess.ThreePlayer;

/// <summary>Move input and output for the three-player board, where files run a–l and ranks 1–12.</summary>
public static partial class TriNotation
{
    private const string SquarePattern = "[a-l](?:1[0-2]|[1-9])";

    [GeneratedRegex($@"^({SquarePattern})[-x]?({SquarePattern})=?([qrbn])?$", RegexOptions.IgnoreCase)]
    private static partial Regex LongAlgebraic();

    [GeneratedRegex($@"^([NBRQK])?({SquarePattern}|[a-l])?x?({SquarePattern})(?:=?([NBRQ]))?$")]
    private static partial Regex StandardAlgebraic();

    [GeneratedRegex(@"^[O0]-?[O0](-?[O0])?$", RegexOptions.IgnoreCase)]
    private static partial Regex Castle();

    /// <summary>True if the text has the shape of a three-player move (it may still be illegal).</summary>
    public static bool LooksLikeMove(string text)
    {
        var s = Normalize(text);
        return s.Length is >= 2 and <= 10 &&
               (LongAlgebraic().IsMatch(s) || Castle().IsMatch(s) || StandardAlgebraic().IsMatch(UppercasePieceLetter(s)));
    }

    /// <summary>
    /// Parses long algebraic (d2d4, i7i5, a7a8q) or standard algebraic (Nc3, Rxi9, dxe9, O-O). A piece letter
    /// can be followed by a file or a whole square to say which piece moves. Promotions default to a queen.
    /// </summary>
    public static bool TryParseMove(TriPosition position, string text, out TriMove move, [NotNullWhen(false)] out string? error)
    {
        move = default;
        error = null;
        var s = Normalize(text);
        var legal = position.GenerateLegalMoves();

        if (Castle().IsMatch(s))
        {
            var kingSide = s.Count(c => c is 'O' or 'o' or '0') == 2;
            var side = position.SideToMove;
            var castle = legal.Where(m => m.From == TriSquare.At(side, 4, 0) && m.To == TriSquare.At(side, kingSide ? 6 : 2, 0) &&
                                          position[m.From].Type == PieceType.King).ToList();
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
            var from = TriSquare.Parse(lan.Groups[1].Value);
            var to = TriSquare.Parse(lan.Groups[2].Value);
            var promotion = lan.Groups[3].Success ? PieceTypes.FromLetter(lan.Groups[3].Value[0])!.Value : PieceType.None;
            return PickPromotion(legal.Where(m => m.From == from && m.To == to).ToList(), promotion, s, out move, out error);
        }

        var san = StandardAlgebraic().Match(UppercasePieceLetter(s));
        if (san.Success)
        {
            var pieceType = san.Groups[1].Success ? PieceTypes.FromLetter(san.Groups[1].Value[0])!.Value : PieceType.Pawn;
            var hint = san.Groups[2].Success ? san.Groups[2].Value.ToLowerInvariant() : null;
            var to = TriSquare.Parse(san.Groups[3].Value);
            var promotion = san.Groups[4].Success ? PieceTypes.FromLetter(san.Groups[4].Value[0])!.Value : PieceType.None;

            var candidates = legal.Where(m =>
                    m.To == to &&
                    position[m.From].Type == pieceType &&
                    (hint is null || (hint.Length == 1 ? TriSquare.Name(m.From)[0] == hint[0] : TriSquare.Name(m.From) == hint)))
                .ToList();

            if (candidates.Select(m => m.From).Distinct().Count() > 1)
            {
                var options = string.Join(", ", candidates.Where(m => m.Promotion is PieceType.None or PieceType.Queen)
                    .Select(m => m.ToToken()));
                error = $"`{text.Trim()}` is ambiguous. Did you mean {options}?";
                return false;
            }

            return PickPromotion(candidates, promotion, s, out move, out error);
        }

        error = $"I don't understand the move `{text.Trim()}`. Try something like `d2d4`, `Nc3` or `O-O`.";
        return false;
    }

    /// <summary>Standard algebraic for display. "+" means the move checks at least one king.</summary>
    public static string ToSan(TriPosition position, TriMove move)
    {
        var piece = position[move.From];
        var sb = new StringBuilder();
        var isCastle = piece.Type == PieceType.King && move.From == TriSquare.At(piece.Color, 4, 0) &&
                       TriSquare.Half(move.To) == piece.Color && TriSquare.Rank(move.To) == 0 &&
                       Math.Abs(TriSquare.File(move.To) - 4) == 2;
        if (isCastle)
        {
            sb.Append(TriSquare.File(move.To) == 6 ? "O-O" : "O-O-O");
        }
        else
        {
            var isCapture = !position[move.To].IsEmpty;
            var rivals = position.GenerateLegalMoves()
                .Where(m => m.To == move.To && m.From != move.From && position[m.From].Type == piece.Type)
                .Select(m => TriSquare.Name(m.From))
                .Distinct()
                .ToList();
            var fromName = TriSquare.Name(move.From);
            var fileOnly = rivals.All(r => r[0] != fromName[0]);

            if (piece.Type == PieceType.Pawn)
            {
                if (isCapture)
                {
                    sb.Append(rivals.Count == 0 || fileOnly ? fromName[..1] : fromName);
                }
            }
            else
            {
                sb.Append(piece.Type.SanLetter());
                if (rivals.Count > 0)
                {
                    sb.Append(fileOnly ? fromName[..1] : fromName);
                }
            }

            if (isCapture)
            {
                sb.Append('x');
            }

            sb.Append(TriSquare.Name(move.To));
            if (move.Promotion != PieceType.None)
            {
                sb.Append('=').Append(move.Promotion.SanLetter());
            }
        }

        var after = position.Apply(move);
        if (TriColors.All.Any(c => c != piece.Color && after.IsInCheck(c)))
        {
            sb.Append('+');
        }

        return sb.ToString();
    }

    private static bool PickPromotion(List<TriMove> candidates, PieceType promotion, string text, out TriMove move, [NotNullWhen(false)] out string? error)
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

    /// <summary>
    /// People often type "nc3" or "qxd7". Lowercase b and k are also file letters here, so they stay as typed.
    /// </summary>
    private static string UppercasePieceLetter(string s) =>
        s.Length > 2 && "nrq".Contains(s[0]) && !LongAlgebraic().IsMatch(s) ? char.ToUpperInvariant(s[0]) + s[1..] : s;
}
