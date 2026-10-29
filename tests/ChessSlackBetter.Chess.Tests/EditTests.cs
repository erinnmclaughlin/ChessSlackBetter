namespace ChessSlackBetter.Chess.Tests;

public class EditTests
{
    private static SquareEdit[] Edits(params string[] edits) => edits.Select(SquareEdit.Parse).ToArray();

    [Fact]
    public void Edits_replace_a_move_and_pass_the_turn()
    {
        var game = new Game();
        Assert.True(game.TryPlay("e4", out _, out _));
        Assert.True(game.TryPlay("e5", out _, out _));

        Assert.True(game.TryApplyEdits(Edits("e4=B"), out var error), error);

        Assert.Equal(new Piece(PieceType.Bishop, PieceColor.White), game.Current[Square.Parse("e4")]);
        Assert.Equal(PieceColor.Black, game.Current.SideToMove);
        Assert.True(game.LastMove!.IsDiceRoll);
        Assert.Equal("1. e4 e5 2. {dice e4=B}", game.MovesSan());
    }

    [Fact]
    public void Dice_games_replay_from_tokens()
    {
        var game = new Game();
        game.TryPlay("e4", out _, out _);
        game.TryPlay("d5", out _, out _);
        game.TryApplyEdits(Edits("e4=-", "d5=-"), out _);
        game.TryPlay("Nf6", out _, out _);

        var tokens = game.Moves.Select(m => m.ToToken()).ToList();
        Assert.Equal(["e2e4", "d7d5", "dice:e4=-,d5=-", "g8f6"], tokens);
        Assert.Equal(game.Current.ToFen(), Game.Replay(tokens).Current.ToFen());
    }

    [Fact]
    public void Removing_a_rook_drops_its_castling_right()
    {
        var position = Position.Start.WithEdits(Edits("h1=-"));
        Assert.Equal(CastlingRights.WhiteQueenSide | CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide, position.Castling);
    }

    [Theory]
    [InlineData("e1=-", "Kings")]
    [InlineData("e4=K", "Kings")]
    [InlineData("a8=P", "Pawns")]
    public void Rejects_illegal_edits(string edit, string expectedError)
    {
        Assert.False(Game.ValidateEdits(Position.Start, Edits(edit), out var error));
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void Rejects_edits_that_leave_your_king_in_check()
    {
        // Removing the f2 pawn exposes the white king to the queen on h4.
        var position = Position.FromFen("rnb1kbnr/pppp1ppp/8/4p3/7q/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1");
        Assert.False(Game.ValidateEdits(position, Edits("f2=-"), out var error));
        Assert.Contains("your own king in check", error);
    }

    [Fact]
    public void Rejects_checkmate_by_dice()
    {
        // A white rook appearing on a8 would mate the boxed-in black king.
        var position = Position.FromFen("6k1/5ppp/8/8/8/8/8/6K1 w - - 0 1");
        Assert.False(Game.ValidateEdits(position, Edits("a8=R"), out var error));
        Assert.Contains("checkmate", error);
    }
}
