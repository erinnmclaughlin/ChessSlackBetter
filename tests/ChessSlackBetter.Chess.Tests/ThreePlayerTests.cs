using System.Text;
using ChessSlackBetter.Chess.ThreePlayer;

namespace ChessSlackBetter.Chess.Tests;

public class ThreePlayerTests
{
    [Fact]
    public void Board_has_96_uniquely_named_squares()
    {
        var names = Enumerable.Range(0, TriSquare.Count).Select(TriSquare.Name).ToList();
        Assert.Equal(96, names.Distinct().Count());
        Assert.Equal("a1", names[TriSquare.At(TriColor.White, 0, 0)]);
        Assert.Equal("l8", names[TriSquare.At(TriColor.Black, 0, 0)]);
        Assert.Equal("d5", names[TriSquare.At(TriColor.Black, 4, 3)]);
        Assert.Equal("h12", names[TriSquare.At(TriColor.Red, 0, 0)]);
        Assert.Equal("i9", names[TriSquare.At(TriColor.Red, 4, 3)]);
        Assert.True(TriSquare.TryParse("L12", out var l12));
        Assert.Equal(TriSquare.At(TriColor.Red, 7, 0), l12);
    }

    [Theory]
    [InlineData("d4", 0, 1, "d5")]
    [InlineData("e4", 0, 1, "e9")]
    [InlineData("i5", 0, 1, "i9")]
    [InlineData("c4", 1, 1, "d5")]
    [InlineData("d4", 1, 1, "e9 i5")]
    [InlineData("e4", -1, 1, "d5 i9")]
    [InlineData("a4", -1, 1, "")]
    public void Files_carry_on_through_the_middle(string from, int df, int dr, string expected)
    {
        var targets = TriSquare.Step(TriSquare.Parse(from), df, dr).Select(t => TriSquare.Name(t.Square));
        Assert.Equal(expected, string.Join(' ', targets.Order()));
    }

    [Fact]
    public void Every_side_starts_with_twenty_moves()
    {
        foreach (var color in TriColors.All)
        {
            Assert.Equal(20, TriPosition.Start.WithSideToMove(color).GenerateLegalMoves().Count);
        }
    }

    [Fact]
    public void A_bishop_through_the_centre_can_go_either_way()
    {
        var position = Pos(TriColor.White, ("e1", "wk"), ("d4", "wb"), ("i8", "bk"), ("i12", "rk"));
        var targets = position.GenerateLegalMoves().Where(m => TriSquare.Name(m.From) == "d4").Select(m => TriSquare.Name(m.To)).ToHashSet();

        Assert.Superset(new HashSet<string> { "c3", "b2", "e3", "c5", "b6", "i5", "j6", "k7", "e9", "f10", "g11", "h12" }, targets);
        Assert.Contains("l8", targets);
        Assert.DoesNotContain("i8", targets);
    }

    [Fact]
    public void Pawns_cross_the_middle_and_promote_on_an_opponents_back_rank()
    {
        var game = new TriGame(Pos(TriColor.White, ("e1", "wk"), ("a7", "wp"), ("l8", "bk"), ("i12", "rk")));

        Assert.True(game.TryPlay("a7a8n", out var played, out var error), error);

        Assert.Equal("a8=N", played.San);
        Assert.Equal(new TriPiece(PieceType.Knight, TriColor.White), game.Current[TriSquare.Parse("a8")]);
        Assert.Equal(TriColor.Black, game.Current.SideToMove);

        var crossing = new TriGame();
        Assert.True(crossing.TryPlay("d4", out _, out _));
        Assert.True(crossing.TryPlay("i5", out _, out _));
        Assert.True(crossing.TryPlay("e10", out _, out _));
        Assert.True(crossing.TryPlay("d5", out _, out error), error);
        Assert.Equal(new TriPiece(PieceType.Pawn, TriColor.White), crossing.Current[TriSquare.Parse("d5")]);
    }

    [Fact]
    public void Castling_works_in_each_players_own_frame()
    {
        var position = Pos(TriColor.Red, ("e1", "wk"), ("l8", "bk"), ("i12", "rk"), ("l12", "rr"), ("h12", "rr"));
        var game = new TriGame(position);

        Assert.True(game.TryPlay("O-O", out var played, out var error), error);

        Assert.Equal("O-O", played.San);
        Assert.Equal(PieceType.King, game.Current[TriSquare.Parse("k12")].Type);
        Assert.Equal(PieceType.Rook, game.Current[TriSquare.Parse("j12")].Type);
    }

    [Fact]
    public void A_checkmated_player_is_eliminated_and_the_last_one_standing_wins()
    {
        // Black's king is boxed in by its own pawns; a white rook down the a-file mates it along rank 8.
        var game = new TriGame(Pos(TriColor.White,
            ("e1", "wk"), ("a1", "wr"),
            ("l8", "bk"), ("l7", "bp"), ("k7", "bp"),
            ("i12", "rk"), ("h11", "rp")));

        Assert.True(game.TryPlay("Ra8", out var played, out var error), error);

        Assert.Equal("Ra8+", played.San);
        var out1 = Assert.Single(game.Eliminations);
        Assert.Equal(new TriElimination(TriColor.Black, Termination.Checkmate, TriColor.White), out1);
        Assert.False(game.Current.IsAlive(TriColor.Black));
        Assert.True(game.Current[TriSquare.Parse("l7")].IsEmpty);
        Assert.Equal(TriColor.Red, game.Current.SideToMove);
        Assert.False(game.IsOver);

        game.Resign(TriColor.Red);

        Assert.True(game.IsOver);
        Assert.Equal(Termination.Resignation, game.Termination);
        Assert.Equal(["white", "red", "black"], game.Ranking.Select(place => string.Join(",", place.Select(c => c.Name()))));
    }

    [Fact]
    public void Resigning_out_of_turn_keeps_the_turn_with_whoever_had_it()
    {
        var game = new TriGame();
        game.TryPlay("d4", out _, out _);

        game.Resign(TriColor.Red);

        Assert.Equal(TriColor.Black, game.Current.SideToMove);
        Assert.True(game.TryPlay("i5", out _, out _));
        Assert.Equal(TriColor.White, game.Current.SideToMove);
    }

    [Fact]
    public void Games_replay_from_tokens()
    {
        var game = new TriGame();
        game.TryPlay("d4", out _, out _);
        game.TryPlay("Nj6", out _, out _);
        game.Resign(TriColor.White);
        game.TryPlay("f10", out _, out _);

        var replayed = TriGame.Replay(game.Tokens);

        Assert.Equal(game.Current.ToCode(), replayed.Current.ToCode());
        Assert.Equal(["d2d4", "k8j6", "resign:w", "f11f10"], replayed.Tokens);
        Assert.Equal(TriColor.Black, replayed.Current.SideToMove);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Random_games_stay_consistent(int seed)
    {
        var random = new Random(seed);
        var game = new TriGame();
        for (var ply = 0; ply < 400 && !game.IsOver; ply++)
        {
            var legal = game.Current.GenerateLegalMoves();
            Assert.NotEmpty(legal);
            Assert.False(game.Current.IsInCheck() && legal.Any(m => game.Current.Apply(m).IsInCheck(game.Current.SideToMove)));
            Assert.True(game.TryPlay(legal[random.Next(legal.Count)].ToToken(), out _, out var error), error);
        }

        var replayed = TriGame.Replay(game.Tokens);
        Assert.Equal(game.Current.ToCode(), replayed.Current.ToCode());
        Assert.Equal(game.Eliminations, replayed.Eliminations);
        Assert.Equal(TriPosition.FromCode(game.Current.ToCode()).ToCode(), game.Current.ToCode());
    }

    [Fact]
    public void Dice_edits_replace_a_move_and_replay()
    {
        var game = new TriGame();

        Assert.True(game.TryApplyEdits([TriSquareEdit.Parse("i8=bp"), TriSquareEdit.Parse("e3=wn")], out var error), error);

        Assert.Equal(TriColor.Black, game.Current.SideToMove);
        Assert.Equal("{dice i8=bp e3=wn}", game.LastMove!.San);
        Assert.Equal(["dice:i8=bp,e3=wn"], game.Tokens);
        Assert.Equal(game.Current.ToCode(), TriGame.Replay(game.Tokens).Current.ToCode());
    }

    [Fact]
    public void Dice_cant_touch_kings_or_checkmate_anyone()
    {
        Assert.False(TriGame.ValidateEdits(TriPosition.Start, [TriSquareEdit.Parse("d8=-")], out var error));
        Assert.Contains("Kings", error);

        Assert.False(TriGame.ValidateEdits(TriPosition.Start, [TriSquareEdit.Parse("e2=-"), TriSquareEdit.Parse("e3=bq")], out error));
        Assert.Contains("own king in check", error);

        Assert.False(TriGame.ValidateEdits(TriPosition.Start, [TriSquareEdit.Parse("a8=wp")], out error));
        Assert.Contains("back rank", error);

        var boxedIn = Pos(TriColor.White,
            ("e1", "wk"), ("a1", "wr"),
            ("l8", "bk"), ("l7", "bp"), ("k7", "bp"),
            ("i12", "rk"), ("h11", "rp"));
        Assert.False(TriGame.ValidateEdits(boxedIn, [TriSquareEdit.Parse("a1=-"), TriSquareEdit.Parse("a8=wr")], out error));
        Assert.Contains("can't checkmate", error);
    }

    [Fact]
    public void Position_codes_round_trip()
    {
        var code = TriPosition.Start.ToCode();
        Assert.Equal(code, TriPosition.FromCode(code).ToCode());
        Assert.StartsWith("w_63_wrwnwbwqwkwbwnwr", code);
    }

    [Theory]
    [InlineData("d2d4", true)]
    [InlineData("i7i5", true)]
    [InlineData("Nc3", true)]
    [InlineData("nj6", true)]
    [InlineData("Rxl12", true)]
    [InlineData("O-O", true)]
    [InlineData("hello", false)]
    [InlineData("m4", false)]
    [InlineData("d13", false)]
    public void Recognises_move_shapes(string text, bool expected)
    {
        Assert.Equal(expected, TriNotation.LooksLikeMove(text));
    }

    [Fact]
    public void Reports_illegal_and_ambiguous_moves()
    {
        Assert.False(TriNotation.TryParseMove(TriPosition.Start, "d2d5", out _, out var error));
        Assert.Contains("isn't a legal move", error);

        var position = Pos(TriColor.White, ("e1", "wk"), ("b1", "wn"), ("f3", "wn"), ("l8", "bk"), ("i12", "rk"));
        Assert.False(TriNotation.TryParseMove(position, "Nd2", out _, out error));
        Assert.Contains("ambiguous", error);
        Assert.True(TriNotation.TryParseMove(position, "Nbd2", out var move, out error), error);
        Assert.Equal("b1d2", move.ToToken());
    }

    private static TriPosition Pos(TriColor side, params (string Square, string Piece)[] pieces)
    {
        var board = new string?[TriSquare.Count];
        foreach (var (square, piece) in pieces)
        {
            board[TriSquare.Parse(square)] = piece;
        }

        var sb = new StringBuilder($"{TriColors.Letter(side)}_63_");
        var empty = 0;
        foreach (var piece in board)
        {
            if (piece is null)
            {
                empty++;
                continue;
            }

            if (empty > 0)
            {
                sb.Append(empty);
                empty = 0;
            }

            sb.Append(piece);
        }

        if (empty > 0)
        {
            sb.Append(empty);
        }

        return TriPosition.FromCode(sb.ToString());
    }
}
