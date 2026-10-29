namespace ChessSlackBetter.Chess.Tests;

public class GameTests
{
    private static Game Play(params string[] moves)
    {
        var game = new Game();
        foreach (var m in moves)
        {
            Assert.True(game.TryPlay(m, out _, out var error), $"{m}: {error}");
        }

        return game;
    }

    [Fact]
    public void Fools_mate_is_checkmate()
    {
        var game = Play("f3", "e5", "g4", "Qh4");
        Assert.Equal(GameOutcome.BlackWins, game.Outcome);
        Assert.Equal(Termination.Checkmate, game.Termination);
        Assert.Equal("Qh4#", game.LastMove!.San);
        Assert.False(game.TryPlay("e4", out _, out _));
    }

    [Fact]
    public void Threefold_repetition_is_a_draw()
    {
        var game = Play("Nf3", "Nf6", "Ng1", "Ng8", "Nf3", "Nf6", "Ng1", "Ng8");
        Assert.Equal(GameOutcome.Draw, game.Outcome);
        Assert.Equal(Termination.ThreefoldRepetition, game.Termination);
    }

    [Fact]
    public void Stalemate_is_a_draw()
    {
        // Sam Loyd's 10-move stalemate.
        var game = Play("e3", "a5", "Qh5", "Ra6", "Qxa5", "h5", "h4", "Rah6", "Qxc7", "f6",
            "Qxd7+", "Kf7", "Qxb7", "Qd3", "Qxb8", "Qh7", "Qxc8", "Kg6", "Qe6");
        Assert.Equal(Termination.Stalemate, game.Termination);
        Assert.Equal(GameOutcome.Draw, game.Outcome);
    }

    [Fact]
    public void Resignation_awards_the_opponent()
    {
        var game = Play("e4");
        game.Resign(PieceColor.Black);
        Assert.Equal(GameOutcome.WhiteWins, game.Outcome);
        Assert.Equal("1-0", game.ResultString);
    }

    [Fact]
    public void En_passant_removes_the_captured_pawn()
    {
        var game = Play("e4", "a6", "e5", "d5", "exd6");
        Assert.True(game.Current[Square.Parse("d5")].IsEmpty);
        Assert.Equal("exd6", game.LastMove!.San);
    }

    [Fact]
    public void Castling_moves_the_rook_and_drops_rights()
    {
        var game = Play("e4", "e5", "Nf3", "Nc6", "Bc4", "Bc5", "O-O");
        Assert.Equal(PieceType.Rook, game.Current[Square.Parse("f1")].Type);
        Assert.Equal(PieceType.King, game.Current[Square.Parse("g1")].Type);
        Assert.Equal(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide, game.Current.Castling);
    }

    [Fact]
    public void Rebuilds_from_uci_and_writes_pgn()
    {
        var game = Game.FromUci(["e2e4", "e7e5", "g1f3"]);
        Assert.Equal("1. e4 e5 2. Nf3", game.MovesSan());
        var pgn = game.ToPgn(new Dictionary<string, string> { ["White"] = "alice", ["Black"] = "bob" });
        Assert.Contains("[White \"alice\"]", pgn);
        Assert.EndsWith("1. e4 e5 2. Nf3 *", pgn);
    }

    [Theory]
    [InlineData("4k3/8/8/8/8/8/8/4K3 w - - 0 1", true)]
    [InlineData("4k3/8/8/8/8/8/8/4KN2 w - - 0 1", true)]
    [InlineData("4kb2/8/8/8/8/8/8/2B1K3 w - - 0 1", true)]
    [InlineData("4k1b1/8/8/8/8/8/8/2B1K3 w - - 0 1", false)]
    [InlineData("4k3/8/8/8/8/8/8/3NKN2 w - - 0 1", false)]
    [InlineData("4k3/8/8/8/8/8/4P3/4K3 w - - 0 1", false)]
    public void Detects_insufficient_material(string fen, bool expected)
    {
        Assert.Equal(expected, Position.FromFen(fen).IsInsufficientMaterial());
    }
}
