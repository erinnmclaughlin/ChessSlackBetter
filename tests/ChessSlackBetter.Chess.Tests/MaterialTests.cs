namespace ChessSlackBetter.Chess.Tests;

public class MaterialTests
{
    [Fact]
    public void Start_position_is_even_with_nothing_lost()
    {
        var material = Material.Of(Position.Start);
        Assert.Equal(39, material.WhitePoints);
        Assert.Equal(39, material.BlackPoints);
        Assert.Equal(0, material.Advantage);
        Assert.Empty(material.WhiteLost);
        Assert.Empty(material.BlackLost);
    }

    [Fact]
    public void Captures_are_counted_and_valued()
    {
        var game = new Game();
        foreach (var move in new[] { "e4", "d5", "exd5", "Qxd5", "Nc3", "Qxa2", "Rxa2" })
        {
            Assert.True(game.TryPlay(move, out _, out var error), error);
        }

        var material = Material.Of(game.Current);
        Assert.Equal([PieceType.Pawn, PieceType.Pawn], material.WhiteLost);
        Assert.Equal([PieceType.Queen, PieceType.Pawn], material.BlackLost);
        Assert.Equal(8, material.Advantage);
    }

    [Fact]
    public void Promotion_is_not_a_lost_pawn()
    {
        // White has promoted a pawn to a second queen; black is missing a rook.
        var material = Material.Of(Position.FromFen("1nbqkbnr/pppppppp/8/8/8/8/1PPPPPPP/RNBQKBNQ w - - 0 1"));
        Assert.Equal([PieceType.Rook], material.WhiteLost);
        Assert.Equal([PieceType.Rook], material.BlackLost);
        Assert.Equal(8, material.Advantage);
    }
}
