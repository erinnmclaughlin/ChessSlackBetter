using System.Text.Json.Nodes;
using ChessSlackBetter.Chess;

namespace ChessSlackBetter.Bot.Games;

public static class ActionIds
{
    public const string Accept = "chess_accept";
    public const string Decline = "chess_decline";
    public const string Resign = "chess_resign";
    public const string OfferDraw = "chess_offer_draw";
    public const string AcceptDraw = "chess_accept_draw";
    public const string DeclineDraw = "chess_decline_draw";
}

public sealed record SlackContent(string Text, JsonArray Blocks);

/// <summary>Builds the Block Kit payloads the bot posts.</summary>
public static class Messages
{
    public const string HelpText = """
        *ChessBetterBot* — play chess in a Slack thread.

        *Start a game*
        • `@ChessBetterBot challenge @someone` — random colours
        • `@ChessBetterBot challenge @someone white` (or `black`) — pick your colour
        The challenge gets its own thread; your opponent accepts with the button or `@ChessBetterBot accept`.

        *In the game thread*
        • Make a move: `@ChessBetterBot e2e4`, `@ChessBetterBot Nf3`, or just type `e4` / `Nf3` / `O-O`
          Long notation works for everything: castle with `e1g1`, promote with `c7c8q` (q, r, b or n)
        • `@ChessBetterBot resign` · `@ChessBetterBot draw` (offer or accept) · `@ChessBetterBot decline draw`
        • `@ChessBetterBot abort` — cancel before both players have moved
        • `@ChessBetterBot board` — show the board again · `@ChessBetterBot pgn` — export / analyse

        *Anywhere*
        • `@ChessBetterBot games` — your open games · `@ChessBetterBot stats [@someone]` · `@ChessBetterBot leaderboard`
        """;

    public static SlackContent ChallengeCard(string challengerId, string opponentId, string whiteId)
    {
        var colour = challengerId == whiteId ? "white" : "black";
        var text = $"<@{challengerId}> has challenged <@{opponentId}> to a game of chess!";
        return new SlackContent(text,
        [
            Section($":chess_pawn: {text}\n<@{challengerId}> will play *{colour}*."),
            Actions(
                Button("Accept", ActionIds.Accept, style: "primary"),
                Button("Decline", ActionIds.Decline, style: "danger")),
            Context($"<@{opponentId}>, you can also reply in the thread with `@ChessBetterBot accept` or `@ChessBetterBot decline`."),
        ]);
    }

    /// <summary>The top-level card once the challenge has been answered: in progress, declined or finished.</summary>
    public static SlackContent StatusCard(GameRecord record, Game? game)
    {
        var players = $"<@{record.WhiteId}> (white) vs <@{record.BlackId}> (black)";
        var text = record.State switch
        {
            GameState.Active => $":chess_pawn: {players} — game in progress. Follow along in the thread.",
            GameState.Declined when record.Termination == Termination.Abandoned =>
                $":no_entry_sign: <@{record.ChallengerId}> withdrew the challenge to <@{record.OpponentId}>.",
            GameState.Declined => $":no_entry_sign: <@{record.OpponentId}> declined <@{record.ChallengerId}>'s challenge.",
            GameState.Finished when game is not null =>
                $":checkered_flag: {players} — *{game.ResultString}* · {DescribeResult(record, game)} after {MoveCountText(game)}.",
            _ => $":chess_pawn: {players}",
        };
        return new SlackContent(text, [Section(text)]);
    }

    public static SlackContent Board(GameRecord record, Game game, string? publicBaseUrl)
    {
        var position = game.Current;
        var toMove = record.PlayerFor(position.SideToMove);
        var colour = position.SideToMove == PieceColor.White ? "white" : "black";
        var header = $"*Move {position.FullmoveNumber}* · <@{toMove}>, it's your turn ({colour}).";
        if (position.IsInCheck())
        {
            header += " :warning: *Check!*";
        }

        if (LastMoveText(game) is { } last)
        {
            header += $"\nLast move: `{last}`";
        }

        var blocks = new JsonArray { Section(header) };
        blocks.Add(BoardBlock(game, position.SideToMove, publicBaseUrl));
        blocks.Add(Actions(
            Button("Offer draw", ActionIds.OfferDraw),
            Button("Resign", ActionIds.Resign, style: "danger", confirm: ("Resign?", "Are you sure you want to resign this game?", "Resign"))));
        blocks.Add(Context("Reply with a move like `e4`, `Nf3`, `O-O` or `e7e8q`. `@ChessBetterBot help` for more."));

        return new SlackContent($"<@{toMove}>, it's your move ({colour}).", blocks);
    }

    public static SlackContent GameOver(GameRecord record, Game game, string pgn, string analysisUrl, string? publicBaseUrl)
    {
        var description = DescribeResult(record, game);
        var text = $":checkered_flag: {description} *{game.ResultString}*";
        var blocks = new JsonArray
        {
            Section($"{text}\n<@{record.WhiteId}> (white) vs <@{record.BlackId}> (black) · {MoveCountText(game)}"),
        };
        if (game.Moves.Count > 0)
        {
            blocks.Add(BoardBlock(game, PieceColor.White, publicBaseUrl));
            foreach (var section in CodeSections(pgn))
            {
                blocks.Add(section);
            }

            blocks.Add(Actions(LinkButton(":mag: Analyze on lichess", analysisUrl)));
        }

        return new SlackContent($"{description} {game.ResultString}", blocks);
    }

    public static SlackContent DrawOffer(string offeredBy, string offeredTo)
    {
        var text = $":handshake: <@{offeredBy}> offers a draw. <@{offeredTo}>, do you accept?";
        return new SlackContent(text,
        [
            Section(text),
            Actions(
                Button("Accept draw", ActionIds.AcceptDraw, style: "primary"),
                Button("Decline", ActionIds.DeclineDraw)),
        ]);
    }

    public static SlackContent Pgn(string pgn, string analysisUrl)
    {
        var blocks = new JsonArray();
        foreach (var section in CodeSections(pgn))
        {
            blocks.Add(section);
        }

        blocks.Add(Actions(LinkButton(":mag: Analyze on lichess", analysisUrl)));
        return new SlackContent("Game PGN", blocks);
    }

    public static string DescribeResult(GameRecord record, Game game)
    {
        var winner = game.Outcome switch
        {
            GameOutcome.WhiteWins => record.WhiteId,
            GameOutcome.BlackWins => record.BlackId,
            _ => null,
        };
        var loser = winner is null ? null : record.OpponentOf(winner);

        return game.Termination switch
        {
            Termination.Checkmate => $"Checkmate! <@{winner}> wins.",
            Termination.Resignation => $"<@{loser}> resigned. <@{winner}> wins.",
            Termination.Stalemate => "Stalemate — it's a draw.",
            Termination.DrawAgreement => "Draw agreed.",
            Termination.ThreefoldRepetition => "Draw by threefold repetition.",
            Termination.FiftyMoveRule => "Draw by the fifty-move rule.",
            Termination.InsufficientMaterial => "Draw — insufficient material to mate.",
            Termination.Abandoned => "Game aborted.",
            _ => "Game over.",
        };
    }

    public static string BoardImageUrl(string publicBaseUrl, Game game, PieceColor perspective)
    {
        var url = $"{publicBaseUrl.TrimEnd('/')}/board.png?fen={Uri.EscapeDataString(game.Current.ToFen())}" +
                  $"&pov={(perspective == PieceColor.White ? 'w' : 'b')}";
        return game.LastMove is { } last ? $"{url}&last={last.Move.ToUci()}" : url;
    }

    private static JsonNode BoardBlock(Game game, PieceColor perspective, string? publicBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return Section($"```{Rendering.BoardRenderer.RenderText(game.Current, perspective)}```");
        }

        return new JsonObject
        {
            ["type"] = "image",
            ["image_url"] = BoardImageUrl(publicBaseUrl, game, perspective),
            ["alt_text"] = $"Chess board: {game.Current.ToFen()}",
        };
    }

    private static string? LastMoveText(Game game)
    {
        if (game.LastMove is not { } last)
        {
            return null;
        }

        var index = game.Moves.Count - 1;
        var number = index / 2 + 1;
        return index % 2 == 0 ? $"{number}. {last.San}" : $"{number}... {last.San}";
    }

    private static string MoveCountText(Game game)
    {
        var moves = (game.Moves.Count + 1) / 2;
        return moves == 1 ? "1 move" : $"{moves} moves";
    }

    /// <summary>Splits text into code-formatted sections that fit Slack's 3000 character limit per block.</summary>
    private static IEnumerable<JsonObject> CodeSections(string text)
    {
        const int max = 2900;
        var remaining = text;
        while (remaining.Length > max)
        {
            var cut = remaining.LastIndexOf(' ', max);
            cut = cut <= 0 ? max : cut;
            yield return Section($"```{remaining[..cut]}```");
            remaining = remaining[cut..].TrimStart();
        }

        yield return Section($"```{remaining}```");
    }

    private static JsonObject Section(string mrkdwn) => new()
    {
        ["type"] = "section",
        ["text"] = new JsonObject { ["type"] = "mrkdwn", ["text"] = mrkdwn },
    };

    private static JsonObject Context(string mrkdwn) => new()
    {
        ["type"] = "context",
        ["elements"] = new JsonArray { new JsonObject { ["type"] = "mrkdwn", ["text"] = mrkdwn } },
    };

    private static JsonObject Actions(params JsonObject[] elements) => new()
    {
        ["type"] = "actions",
        ["elements"] = new JsonArray(elements.Cast<JsonNode>().ToArray()),
    };

    private static JsonObject Button(string label, string actionId, string? style = null, (string Title, string Text, string Confirm)? confirm = null)
    {
        var button = new JsonObject
        {
            ["type"] = "button",
            ["text"] = PlainText(label),
            ["action_id"] = actionId,
        };
        if (style is not null) button["style"] = style;
        if (confirm is { } c)
        {
            button["confirm"] = new JsonObject
            {
                ["title"] = PlainText(c.Title),
                ["text"] = PlainText(c.Text),
                ["confirm"] = PlainText(c.Confirm),
                ["deny"] = PlainText("Keep playing"),
                ["style"] = "danger",
            };
        }

        return button;
    }

    private static JsonObject LinkButton(string label, string url) => new()
    {
        ["type"] = "button",
        ["text"] = PlainText(label),
        ["action_id"] = "chess_link",
        ["url"] = url,
    };

    private static JsonObject PlainText(string text) => new() { ["type"] = "plain_text", ["text"] = text, ["emoji"] = true };
}
