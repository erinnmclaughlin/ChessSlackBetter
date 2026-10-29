namespace ChessSlackBetter.Chess;

public enum PieceColor
{
    White,
    Black,
}

public enum PieceType
{
    None,
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King,
}

[Flags]
public enum CastlingRights
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    All = WhiteKingSide | WhiteQueenSide | BlackKingSide | BlackQueenSide,
}

public readonly record struct Piece(PieceType Type, PieceColor Color)
{
    public static readonly Piece Empty = new(PieceType.None, PieceColor.White);

    public bool IsEmpty => Type == PieceType.None;

    /// <summary>FEN letter: uppercase for white, lowercase for black.</summary>
    public char ToFenChar()
    {
        var c = Type switch
        {
            PieceType.Pawn => 'p',
            PieceType.Knight => 'n',
            PieceType.Bishop => 'b',
            PieceType.Rook => 'r',
            PieceType.Queen => 'q',
            PieceType.King => 'k',
            _ => throw new InvalidOperationException("Empty square has no FEN character."),
        };
        return Color == PieceColor.White ? char.ToUpperInvariant(c) : c;
    }

    public static Piece FromFenChar(char c)
    {
        var color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
        var type = PieceTypes.FromLetter(c) ?? throw new FormatException($"Invalid piece character '{c}'.");
        return new Piece(type, color);
    }
}

public static class PieceTypes
{
    public static PieceType? FromLetter(char c) => char.ToLowerInvariant(c) switch
    {
        'p' => PieceType.Pawn,
        'n' => PieceType.Knight,
        'b' => PieceType.Bishop,
        'r' => PieceType.Rook,
        'q' => PieceType.Queen,
        'k' => PieceType.King,
        _ => null,
    };

    /// <summary>Uppercase SAN letter; empty for pawns.</summary>
    public static string SanLetter(this PieceType type) => type switch
    {
        PieceType.Knight => "N",
        PieceType.Bishop => "B",
        PieceType.Rook => "R",
        PieceType.Queen => "Q",
        PieceType.King => "K",
        _ => "",
    };

    public static PieceColor Opponent(this PieceColor color) =>
        color == PieceColor.White ? PieceColor.Black : PieceColor.White;
}

/// <summary>Squares are 0..63 with a1 = 0, b1 = 1, ..., h8 = 63.</summary>
public static class Square
{
    public static int File(int square) => square & 7;

    public static int Rank(int square) => square >> 3;

    public static int At(int file, int rank) => rank * 8 + file;

    public static bool IsOnBoard(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8;

    public static string Name(int square) => $"{(char)('a' + File(square))}{(char)('1' + Rank(square))}";

    public static bool TryParse(ReadOnlySpan<char> text, out int square)
    {
        square = -1;
        if (text.Length != 2)
        {
            return false;
        }

        var file = char.ToLowerInvariant(text[0]) - 'a';
        var rank = text[1] - '1';
        if (!IsOnBoard(file, rank))
        {
            return false;
        }

        square = At(file, rank);
        return true;
    }

    public static int Parse(string text) =>
        TryParse(text, out var square) ? square : throw new FormatException($"Invalid square '{text}'.");

    public static bool IsLight(int square) => (File(square) + Rank(square)) % 2 == 1;
}

public readonly record struct Move(int From, int To, PieceType Promotion = PieceType.None)
{
    /// <summary>Long algebraic / UCI notation, e.g. "e2e4" or "e7e8q".</summary>
    public string ToUci()
    {
        var promotion = Promotion == PieceType.None ? "" : Promotion.SanLetter().ToLowerInvariant();
        return Square.Name(From) + Square.Name(To) + promotion;
    }

    public override string ToString() => ToUci();
}
