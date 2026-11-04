namespace ChessSlackBetter.Chess;

/// <summary>
/// Chess960 (Fischer Random) starting positions, numbered 0–959 in the standard (Scharnagl) order.
/// Number 518 is the normal starting position.
/// </summary>
public static class Chess960
{
    public const int Count = 960;
    public const int StandardNumber = 518;

    /// <summary>Where the two knights go among the five squares left after the bishops and queen.</summary>
    private static readonly (int First, int Second)[] KnightSpots =
        [(0, 1), (0, 2), (0, 3), (0, 4), (1, 2), (1, 3), (1, 4), (2, 3), (2, 4), (3, 4)];

    /// <summary>White's back rank from a to h, e.g. "RNBQKBNR" for 518.</summary>
    public static string BackRank(int number)
    {
        if (number is < 0 or >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(number), "Chess960 positions are numbered 0 to 959.");
        }

        var rank = new char[8];
        var n = number;
        rank[2 * (n % 4) + 1] = 'B'; // light-squared bishop on b, d, f or h
        n /= 4;
        rank[2 * (n % 4)] = 'B'; // dark-squared bishop on a, c, e or g
        n /= 4;
        PlaceOnEmpty(rank, n % 6, 'Q');
        n /= 6;

        // Knights take two of the five empty squares; counting the second after the first is placed shifts it by one.
        var (first, second) = KnightSpots[n];
        PlaceOnEmpty(rank, first, 'N');
        PlaceOnEmpty(rank, second - 1, 'N');

        // The last three empty squares are always rook, king, rook, so the king sits between the rooks.
        PlaceOnEmpty(rank, 0, 'R');
        PlaceOnEmpty(rank, 0, 'K');
        PlaceOnEmpty(rank, 0, 'R');
        return new string(rank);
    }

    public static string StartFen(int number)
    {
        var white = BackRank(number);
        var start = Position.FromFen($"{white.ToLowerInvariant()}/pppppppp/8/8/8/8/PPPPPPPP/{white} w KQkq - 0 1");
        return start.ToFen();
    }

    public static Position StartPosition(int number) => Position.FromFen(StartFen(number));

    private static void PlaceOnEmpty(char[] rank, int emptyIndex, char piece)
    {
        for (var file = 0; file < 8; file++)
        {
            if (rank[file] == '\0' && emptyIndex-- == 0)
            {
                rank[file] = piece;
                return;
            }
        }
    }
}
