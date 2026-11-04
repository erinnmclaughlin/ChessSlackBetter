namespace ChessSlackBetter.Chess;

/// <summary>
/// Material on the board and the pieces each side has lost, inferred from the position alone
/// (so it also copes with dice edits). Pieces beyond the starting set are treated as promoted
/// pawns, so a promotion doesn't look like a captured pawn.
/// </summary>
public sealed record Material(int WhitePoints, int BlackPoints, IReadOnlyList<PieceType> WhiteLost, IReadOnlyList<PieceType> BlackLost)
{
    private static readonly PieceType[] Order = [PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight, PieceType.Pawn];

    /// <summary>Positive when white is ahead.</summary>
    public int Advantage => WhitePoints - BlackPoints;

    public IReadOnlyList<PieceType> Lost(PieceColor color) => color == PieceColor.White ? WhiteLost : BlackLost;

    public int Points(PieceColor color) => color == PieceColor.White ? WhitePoints : BlackPoints;

    public static int Value(PieceType type) => type switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight or PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        _ => 0,
    };

    public static int StartCount(PieceType type) => type switch
    {
        PieceType.Pawn => 8,
        PieceType.Knight or PieceType.Bishop or PieceType.Rook => 2,
        PieceType.Queen => 1,
        _ => 0,
    };

    public static Material Of(Position position)
    {
        var counts = new Dictionary<(PieceColor, PieceType), int>();
        for (var square = 0; square < 64; square++)
        {
            var piece = position[square];
            if (!piece.IsEmpty)
            {
                counts[(piece.Color, piece.Type)] = counts.GetValueOrDefault((piece.Color, piece.Type)) + 1;
            }
        }

        int Count(PieceColor c, PieceType t) => counts.GetValueOrDefault((c, t));

        int PointsFor(PieceColor c) => Order.Sum(t => Count(c, t) * Value(t));

        List<PieceType> LostFor(PieceColor c)
        {
            var lost = new List<PieceType>();
            var extras = 0;
            foreach (var type in Order.Where(t => t != PieceType.Pawn))
            {
                var diff = StartCount(type) - Count(c, type);
                if (diff > 0) lost.AddRange(Enumerable.Repeat(type, diff));
                else extras -= diff;
            }

            var missingPawns = Math.Max(0, StartCount(PieceType.Pawn) - Count(c, PieceType.Pawn) - extras);
            lost.AddRange(Enumerable.Repeat(PieceType.Pawn, missingPawns));
            return lost;
        }

        return new Material(PointsFor(PieceColor.White), PointsFor(PieceColor.Black), LostFor(PieceColor.White), LostFor(PieceColor.Black));
    }

    /// <summary>Unicode chess glyph, e.g. ♛ for a black queen.</summary>
    public static char Glyph(PieceType type, PieceColor color) => (type, color) switch
    {
        (PieceType.King, PieceColor.White) => '♔',
        (PieceType.Queen, PieceColor.White) => '♕',
        (PieceType.Rook, PieceColor.White) => '♖',
        (PieceType.Bishop, PieceColor.White) => '♗',
        (PieceType.Knight, PieceColor.White) => '♘',
        (PieceType.Pawn, PieceColor.White) => '♙',
        (PieceType.King, _) => '♚',
        (PieceType.Queen, _) => '♛',
        (PieceType.Rook, _) => '♜',
        (PieceType.Bishop, _) => '♝',
        (PieceType.Knight, _) => '♞',
        (PieceType.Pawn, _) => '♟',
        _ => ' ',
    };
}
