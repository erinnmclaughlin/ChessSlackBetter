namespace ChessSlackBetter.Chess.Tests;

public class Chess960Tests
{
    [Theory]
    [InlineData(0, "BBQNNRKR")]
    [InlineData(518, "RNBQKBNR")]
    [InlineData(959, "RKRNNQBB")]
    public void Numbers_follow_the_standard_order(int number, string backRank)
    {
        Assert.Equal(backRank, Chess960.BackRank(number));
    }

    [Fact]
    public void All_960_starts_are_distinct_and_legal()
    {
        var ranks = Enumerable.Range(0, Chess960.Count).Select(Chess960.BackRank).ToList();
        Assert.Equal(Chess960.Count, ranks.Distinct().Count());
        foreach (var rank in ranks)
        {
            var bishops = Enumerable.Range(0, 8).Where(f => rank[f] == 'B').ToList();
            Assert.NotEqual(bishops[0] % 2, bishops[1] % 2);
            var rooks = Enumerable.Range(0, 8).Where(f => rank[f] == 'R').ToList();
            Assert.InRange(rank.IndexOf('K'), rooks[0] + 1, rooks[1] - 1);
        }
    }

    [Fact]
    public void Position_518_is_normal_chess()
    {
        Assert.Equal(Position.StartFen, Chess960.StartFen(Chess960.StandardNumber));
        Assert.True(Chess960.StartPosition(Chess960.StandardNumber).Layout.IsStandard);
    }

    [Fact]
    public void Other_starts_use_rook_files_in_the_fen()
    {
        var start = Chess960.StartPosition(0); // BBQNNRKR
        Assert.Equal("bbqnnrkr/pppppppp/8/8/8/8/PPPPPPPP/BBQNNRKR w HFhf - 0 1", start.ToFen());
        Assert.Equal(new CastlingLayout(6, 7, 5), start.Layout);
        Assert.Equal(20, start.GenerateLegalMoves().Count);
    }

    /// <summary>Reference counts from https://www.chessprogramming.org/Chess960_Perft_Results.</summary>
    [Theory]
    [InlineData("bqnb1rkr/pp3ppp/3ppn2/2p5/5P2/P2P4/NPP1P1PP/BQ1BNRKR w HFhf - 2 9", 3, 12189)]
    [InlineData("2nnrbkr/p1qppppp/8/1ppb4/6PP/3PP3/PPP2P2/BQNNRBKR w HEhe - 1 9", 3, 18002)]
    [InlineData("b1q1rrkb/pppppppp/3nn3/8/P7/1PPP4/4PPPP/BQNNRKRB w GE - 1 9", 3, 10471)]
    [InlineData("bqnb1rkr/pp3ppp/3ppn2/2p5/5P2/P2P4/NPP1P1PP/BQ1BNRKR w HFhf - 2 9", 4, 326672)]
    public void Perft_matches_reference(string fen, int depth, long expected)
    {
        Assert.Equal(expected, Perft(Position.FromFen(fen), depth));
    }

    [Fact]
    public void King_that_is_already_home_castles_by_moving_only_the_rook()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/8/R5KR w HA - 0 1");
        Assert.True(Notation.TryParseMove(position, "O-O", out var move, out _));
        Assert.Equal("g1h1", move.ToUci());
        Assert.Equal("O-O", Notation.ToSan(position, move));

        var after = position.Apply(move);
        Assert.Equal(new Piece(PieceType.King, PieceColor.White), after[Square.Parse("g1")]);
        Assert.Equal(new Piece(PieceType.Rook, PieceColor.White), after[Square.Parse("f1")]);
        Assert.True(after[Square.Parse("h1")].IsEmpty);
        Assert.Equal("-", after.ToFen().Split(' ')[2]);
    }

    [Fact]
    public void Queenside_castle_goes_to_c_and_d_and_can_be_typed_as_the_kings_destination()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/8/R5KR w HA - 0 1");
        Assert.True(Notation.TryParseMove(position, "g1c1", out var move, out _));
        Assert.Equal("g1a1", move.ToUci());
        Assert.Equal("O-O-O", Notation.ToSan(position, move));

        var after = position.Apply(move);
        Assert.Equal("4k3/8/8/8/8/8/8/2KR3R b - - 1 1", after.ToFen());
    }

    [Fact]
    public void King_and_rook_can_swap_squares()
    {
        // King f1, rook g1: castling kingside puts the king on g1 and the rook on f1.
        var position = Position.FromFen("6k1/8/8/8/8/8/8/5KR1 w G - 0 1");
        Assert.True(Notation.TryParseMove(position, "O-O", out var move, out _));
        var after = position.Apply(move);
        Assert.Equal("6k1/8/8/8/8/8/8/5RK1 b - - 1 1", after.ToFen());

        // With a black rook on g8 the king would land in check once its own rook leaves g1.
        Assert.False(Notation.TryParseMove(Position.FromFen("6r1/8/8/8/8/8/8/k4KR1 w G - 0 1"), "O-O", out _, out _));
    }

    [Fact]
    public void A_king_step_that_could_also_be_a_castle_must_be_spelled_out()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/8/5K1R w H - 0 1");
        Assert.False(Notation.TryParseMove(position, "f1g1", out _, out var error));
        Assert.Contains("`O-O` to castle or `Kg1`", error);

        Assert.True(Notation.TryParseMove(position, "Kg1", out var step, out _));
        Assert.Equal("-", position.Apply(step).ToFen().Split(' ')[2]); // a plain king move gives up castling
        Assert.True(Notation.TryParseMove(position, "O-O", out var castle, out _));
        Assert.Equal("f1h1", castle.ToUci());
    }

    [Fact]
    public void Castling_needs_the_path_clear_and_safe()
    {
        // A knight on d1 blocks queenside castling from g1 even though the rook on a1 could otherwise go to d1.
        var blocked = Position.FromFen("4k3/8/8/8/8/8/8/R2N2KR w HA - 0 1");
        Assert.False(Notation.TryParseMove(blocked, "O-O-O", out _, out _));
        Assert.True(Notation.TryParseMove(blocked, "O-O", out _, out _));

        // The king passes over e1 on the way to c1; a rook on e8 attacks it.
        var attacked = Position.FromFen("4r1k1/8/8/8/8/8/8/R5KR w HA - 0 1");
        Assert.False(Notation.TryParseMove(attacked, "O-O-O", out _, out var error));
        Assert.Contains("can't castle", error);
    }

    [Fact]
    public void Moving_a_rook_loses_only_that_side()
    {
        var position = Position.FromFen("4k3/8/8/8/8/8/8/R5KR w HA - 0 1");
        Assert.True(Notation.TryParseMove(position, "Ra2", out var move, out _));
        Assert.Equal("H", position.Apply(move).ToFen().Split(' ')[2]);
    }

    [Fact]
    public void Games_replay_from_their_start_and_export_it()
    {
        var number = Enumerable.Range(0, Chess960.Count).Single(n => Chess960.BackRank(n) == "BBQNRKNR");
        var start = Chess960.StartPosition(number);
        var game = new Game(start);
        foreach (var move in new[] { "Nf3", "Nf6", "O-O", "O-O" })
        {
            Assert.True(game.TryPlay(move, out _, out var error), error);
        }

        Assert.Equal(["g1f3", "g8f6", "f1h1", "f8h8"], game.Moves.Select(m => m.ToToken()));
        var replayed = Game.Replay(game.Moves.Select(m => m.ToToken()), start);
        Assert.Equal(game.Current.ToFen(), replayed.Current.ToFen());
        Assert.Equal("bbqnrrk1/pppppppp/5n2/8/8/5N2/PPPPPPPP/BBQNRRK1 w - - 4 3", replayed.Current.ToFen());
        Assert.Equal("1. Nf3 Nf6 2. O-O O-O", game.MovesSan());

        var pgn = game.ToPgn(new Dictionary<string, string> { ["Variant"] = "Chess960" });
        Assert.Contains("[SetUp \"1\"]", pgn);
        Assert.Contains("[FEN \"bbqnrknr/pppppppp/8/8/8/8/PPPPPPPP/BBQNRKNR w HEhe - 0 1\"]", pgn);
        Assert.DoesNotContain("[FEN", new Game().ToPgn());
    }

    [Fact]
    public void Random_games_survive_fen_round_trips_and_replays()
    {
        var random = new Random(960);
        for (var game = 0; game < 40; game++)
        {
            var start = Chess960.StartPosition(random.Next(Chess960.Count));
            var played = new Game(start);
            for (var ply = 0; ply < 120 && !played.IsOver; ply++)
            {
                var position = played.Current;
                var legal = position.GenerateLegalMoves();
                var reloaded = Position.FromFen(position.ToFen());
                Assert.Equal(legal.Select(m => m.ToUci()).Order(), reloaded.GenerateLegalMoves().Select(m => m.ToUci()).Order());

                // Prefer castling when it's available so the games exercise it.
                var castles = legal.Where(m => position.IsCastling(m, out _)).ToList();
                var move = castles.Count > 0 ? castles[0] : legal[random.Next(legal.Count)];
                Assert.True(played.TryPlay(move.ToUci(), out _, out var error), error);
            }

            var replayed = Game.Replay(played.Moves.Select(m => m.ToToken()), start);
            Assert.Equal(played.Current.ToFen(), replayed.Current.ToFen());
        }
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
