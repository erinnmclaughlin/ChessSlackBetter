namespace ChessSlackBetter.Chess.Tests;

/// <summary>Move-generation node counts from https://www.chessprogramming.org/Perft_Results.</summary>
public class PerftTests
{
    [Theory]
    [InlineData(Position.StartFen, 1, 20)]
    [InlineData(Position.StartFen, 2, 400)]
    [InlineData(Position.StartFen, 3, 8902)]
    [InlineData(Position.StartFen, 4, 197281)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 1, 48)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 2, 2039)]
    [InlineData("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97862)]
    [InlineData("8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43238)]
    [InlineData("r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9467)]
    [InlineData("rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62379)]
    [InlineData("r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 3, 89890)]
    public void Perft_matches_reference(string fen, int depth, long expected)
    {
        Assert.Equal(expected, Perft(Position.FromFen(fen), depth));
    }

    private static long Perft(Position position, int depth)
    {
        var moves = position.GenerateLegalMoves();
        if (depth == 1)
        {
            return moves.Count;
        }

        long nodes = 0;
        foreach (var move in moves)
        {
            nodes += Perft(position.Apply(move), depth - 1);
        }

        return nodes;
    }
}
