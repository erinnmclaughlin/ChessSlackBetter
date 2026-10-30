namespace ChessSlackBetter.Chess.ThreePlayer;

/// <summary>The three sides, in turn order. Each side's half of the board is named after it.</summary>
public enum TriColor
{
    White,
    Black,
    Red,
}

public readonly record struct TriPiece(PieceType Type, TriColor Color)
{
    public static readonly TriPiece Empty = new(PieceType.None, TriColor.White);

    public bool IsEmpty => Type == PieceType.None;

    /// <summary>Two-letter code: colour (w, b, r) then piece (p, n, b, r, q, k), e.g. "rq" for Red's queen.</summary>
    public string ToCode() => $"{TriColors.Letter(Color)}{char.ToLowerInvariant(Type == PieceType.Pawn ? 'p' : Type.SanLetter()[0])}";

    public static TriPiece FromCode(ReadOnlySpan<char> code)
    {
        if (code.Length != 2 || TriColors.FromLetter(code[0]) is not { } color || PieceTypes.FromLetter(code[1]) is not { } type)
        {
            throw new FormatException($"Invalid piece code '{code}'.");
        }

        return new TriPiece(type, color);
    }
}

/// <summary>
/// Puts a piece on a square, or empties it when <see cref="Piece"/> is <see cref="TriPiece.Empty"/>.
/// Text form is "e9=rq" (square, then a piece code) or "e9=-" for an empty square.
/// </summary>
public readonly record struct TriSquareEdit(int Square, TriPiece Piece)
{
    public override string ToString() => $"{TriSquare.Name(Square)}={(Piece.IsEmpty ? "-" : Piece.ToCode())}";

    public static TriSquareEdit Parse(string text)
    {
        var parts = text.Split('=');
        if (parts.Length != 2 || !TriSquare.TryParse(parts[0], out var square))
        {
            throw new FormatException($"Invalid square edit '{text}'.");
        }

        return new TriSquareEdit(square, parts[1] == "-" ? TriPiece.Empty : TriPiece.FromCode(parts[1]));
    }
}

public static class TriColors
{
    public static readonly TriColor[] All = [TriColor.White, TriColor.Black, TriColor.Red];

    /// <summary>The next side in turn order. It also sits on this side's left, across their a–d style quadrant.</summary>
    public static TriColor Next(this TriColor color) => (TriColor)(((int)color + 1) % 3);

    public static TriColor Previous(this TriColor color) => (TriColor)(((int)color + 2) % 3);

    public static char Letter(TriColor color) => color switch
    {
        TriColor.White => 'w',
        TriColor.Black => 'b',
        _ => 'r',
    };

    public static TriColor? FromLetter(char c) => char.ToLowerInvariant(c) switch
    {
        'w' => TriColor.White,
        'b' => TriColor.Black,
        'r' => TriColor.Red,
        _ => null,
    };

    public static string Name(this TriColor color) => color.ToString().ToLowerInvariant();
}

/// <summary>
/// The Yalta three-player board: 96 squares, three halves of 8 files by 4 ranks that meet in the middle.
/// Each half is indexed in its owner's own frame: file 0–7 left to right and rank 0 (back rank) to 3 (the middle),
/// so square = half * 32 + rank * 8 + file. Each half's left quadrant (files 0–3) joins the next side's right
/// quadrant, and its right quadrant joins the previous side's left quadrant; across that join the frame turns
/// round, as if the two quadrants were the halves of a normal 4×8 strip of a chessboard.
/// </summary>
/// <remarks>
/// Square names follow the usual Yalta notation. White has files a–h on ranks 1–4, Black has l–i and d–a
/// (left to right as Black sees them) on ranks 8–5, and Red has h–e and i–l on ranks 12–9. So the a–d files run
/// from White's back rank to Black's, e–h from White's to Red's, and i–l from Black's to Red's.
/// </remarks>
public static class TriSquare
{
    public const int Count = 96;

    private static readonly string[] FileLetters = ["abcdefgh", "lkjidcba", "hgfeijkl"];

    private static readonly string[] Names = Enumerable.Range(0, Count).Select(BuildName).ToArray();

    private static readonly Dictionary<string, int> ByName =
        Enumerable.Range(0, Count).ToDictionary(sq => Names[sq], StringComparer.OrdinalIgnoreCase);

    public static TriColor Half(int square) => (TriColor)(square / 32);

    public static int File(int square) => square % 8;

    public static int Rank(int square) => square % 32 / 8;

    public static int At(TriColor half, int file, int rank) => (int)half * 32 + rank * 8 + file;

    public static string Name(int square) => Names[square];

    public static bool TryParse(string text, out int square) => ByName.TryGetValue(text.Trim(), out square);

    public static int Parse(string text) =>
        TryParse(text, out var square) ? square : throw new FormatException($"Invalid square '{text}'.");

    public static bool IsLight(int square) => (File(square) + Rank(square)) % 2 == 1;

    /// <summary>
    /// One step from a square, with the direction given in that square's half's frame (dr = +1 is towards the
    /// middle). Usually there is one result or none. A diagonal step through the very centre touches the central
    /// squares of both other halves, so it has two. <c>Flipped</c> means the step crossed into a half whose frame
    /// is turned round, so the caller must negate any further directions.
    /// </summary>
    public static IEnumerable<(int Square, bool Flipped)> Step(int square, int df, int dr)
    {
        var half = Half(square);
        var file = File(square) + df;
        var rank = Rank(square) + dr;
        if (file is < 0 or > 7 || rank < 0)
        {
            yield break;
        }

        if (rank <= 3)
        {
            yield return (At(half, file, rank), false);
            yield break;
        }

        var fromLeft = File(square) < 4;
        if (fromLeft == file < 4)
        {
            yield return (At(fromLeft ? half.Next() : half.Previous(), 7 - file, 3), true);
            yield break;
        }

        yield return (At(half.Next(), 7 - file, 3), true);
        yield return (At(half.Previous(), 7 - file, 3), true);
    }

    private static string BuildName(int square)
    {
        var half = (int)Half(square);
        var rank = half switch
        {
            0 => Rank(square) + 1,
            1 => 8 - Rank(square),
            _ => 12 - Rank(square),
        };
        return $"{FileLetters[half][File(square)]}{rank}";
    }
}
