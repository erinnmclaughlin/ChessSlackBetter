namespace ChessSlackBetter.Chess.Tests;

public class NotationTests
{
    [Theory]
    [InlineData("e2e4", "e2e4")]
    [InlineData("e2-e4", "e2e4")]
    [InlineData("e4", "e2e4")]
    [InlineData("Nf3", "g1f3")]
    [InlineData("nf3", "g1f3")]
    [InlineData("g1f3", "g1f3")]
    [InlineData("Nc3+", "b1c3")]
    public void Parses_moves_from_the_start(string input, string expectedUci)
    {
        Assert.True(Notation.TryParseMove(Position.Start, input, out var move, out var error), error);
        Assert.Equal(expectedUci, move.ToUci());
    }

    [Theory]
    [InlineData("e5")]
    [InlineData("e2e5")]
    [InlineData("Nf6")]
    [InlineData("O-O")]
    [InlineData("hello")]
    public void Rejects_illegal_or_garbage_moves(string input)
    {
        Assert.False(Notation.TryParseMove(Position.Start, input, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("O-O", "e1g1")]
    [InlineData("0-0", "e1g1")]
    [InlineData("e1g1", "e1g1")]
    [InlineData("O-O-O", "e1c1")]
    [InlineData("e1c1", "e1c1")]
    public void Parses_castling(string input, string expectedUci)
    {
        var position = Position.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        Assert.True(Notation.TryParseMove(position, input, out var move, out var error), error);
        Assert.Equal(expectedUci, move.ToUci());
    }

    [Theory]
    [InlineData("c7c8q", PieceType.Queen)]
    [InlineData("c7c8n", PieceType.Knight)]
    [InlineData("c8=R", PieceType.Rook)]
    [InlineData("c8B", PieceType.Bishop)]
    [InlineData("c7c8", PieceType.Queen)]
    [InlineData("c8", PieceType.Queen)]
    public void Parses_promotions(string input, PieceType expected)
    {
        var position = Position.FromFen("4k3/2P5/8/8/8/8/8/4K3 w - - 0 1");
        Assert.True(Notation.TryParseMove(position, input, out var move, out var error), error);
        Assert.Equal(expected, move.Promotion);
    }

    [Fact]
    public void Reports_ambiguous_san()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/4K3/R6R w - - 0 1");
        Assert.False(Notation.TryParseMove(position, "Rd1", out _, out var error));
        Assert.Contains("ambiguous", error);
        Assert.True(Notation.TryParseMove(position, "Rad1", out var move, out _));
        Assert.Equal("a1d1", move.ToUci());
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/4K3/R6R w - - 0 1", "a1d1", "Rad1")]
    [InlineData("4k3/8/8/8/8/8/8/R3K2R w K - 0 1", "e1g1", "O-O")]
    [InlineData("4k3/2P5/8/8/8/8/8/4K3 w - - 0 1", "c7c8q", "c8=Q+")]
    [InlineData("rnbqkbnr/ppp1pppp/8/8/3pP3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", "d4e3", "dxe3")]
    [InlineData("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", "a1a8", "Ra8#")]
    [InlineData("4k3/8/8/8/1N3N2/8/8/4K3 w - - 0 1", "b4d5", "Nbd5")]
    [InlineData("4k3/8/8/1N6/8/1N6/8/4K3 w - - 0 1", "b3d4", "N3d4")]
    public void Formats_san(string fen, string uci, string expectedSan)
    {
        var position = Position.FromFen(fen);
        var move = position.GenerateLegalMoves().Single(m => m.ToUci() == uci);
        Assert.Equal(expectedSan, Notation.ToSan(position, move));
    }

    [Theory]
    [InlineData("e4", true)]
    [InlineData("Nxf7+", true)]
    [InlineData("e2e4", true)]
    [InlineData("O-O-O", true)]
    [InlineData("ok", false)]
    [InlineData("nice move", false)]
    [InlineData("lol", false)]
    public void Detects_move_shaped_text(string text, bool expected)
    {
        Assert.Equal(expected, Notation.LooksLikeMove(text));
    }

    [Fact]
    public void Fen_round_trips()
    {
        const string fen = "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1";
        Assert.Equal(fen, Position.FromFen(fen).ToFen());
    }
}
