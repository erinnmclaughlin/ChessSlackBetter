using System.Text;
using ChessSlackBetter.Bot.Slack;
using ChessSlackBetter.Chess;
using Microsoft.Extensions.Options;

namespace ChessSlackBetter.Bot.Games;

/// <summary>A message that mentions the bot, or (when <see cref="IsMention"/> is false) any message in a thread.</summary>
public sealed record IncomingMessage(string ChannelId, string UserId, string Text, string Ts, string? ThreadTs, bool IsMention);

/// <summary>A button click. <see cref="ThreadTs"/> is the game thread the clicked message belongs to.</summary>
public sealed record IncomingAction(string ActionId, string UserId, string ChannelId, string ThreadTs);

public sealed class ChessBotService(
    ISlackApi slack,
    IGameRepository games,
    IAnalysisLinks analysis,
    IOptions<ChessBotOptions> options,
    TimeProvider time,
    ILogger<ChessBotService> logger)
{
    private ChessBotOptions Options => options.Value;

    public async Task HandleMessageAsync(IncomingMessage message, CancellationToken ct = default)
    {
        var botId = await slack.GetBotUserIdAsync(ct);
        if (message.UserId == botId)
        {
            return;
        }

        if (!message.IsMention)
        {
            await HandleUnaddressedThreadMessageAsync(message, botId, ct);
            return;
        }

        var command = CommandParser.Parse(CommandParser.StripMention(message.Text, botId));
        logger.LogInformation("{User} in {Channel}: {Command}", message.UserId, message.ChannelId, command);

        switch (command)
        {
            case HelpCommand:
                await EphemeralAsync(message, Messages.HelpText, ct);
                return;
            case UnknownCommand unknown:
                await EphemeralAsync(message, $"Sorry, I don't understand `{unknown.Text}`. Try `@chessbot help`.", ct);
                return;
            case ChallengeCommand challenge:
                await ChallengeAsync(message, challenge, botId, ct);
                return;
            case GamesCommand:
                await ListGamesAsync(message, ct);
                return;
            case StatsCommand stats:
                await StatsAsync(message, stats.UserId ?? message.UserId, ct);
                return;
            case LeaderboardCommand:
                await LeaderboardAsync(message, ct);
                return;
        }

        // Everything else is about the game in this thread.
        var record = message.ThreadTs is null ? null : await games.GetAsync(message.ChannelId, message.ThreadTs, ct);
        if (record is null)
        {
            await EphemeralAsync(message, "There's no game in this thread. Start one with `@chessbot challenge @someone`.", ct);
            return;
        }

        var reply = (string text) => slack.PostEphemeralAsync(message.ChannelId, message.UserId, record.ThreadTs, text, ct);
        switch (command)
        {
            case AcceptCommand:
                await AcceptAsync(record, message.UserId, reply, ct);
                break;
            case DeclineCommand:
                await DeclineAsync(record, message.UserId, reply, ct);
                break;
            case MoveCommand move:
                await MoveAsync(record, message.UserId, move.Move, reply, ct);
                break;
            case ResignCommand:
                await ResignAsync(record, message.UserId, reply, ct);
                break;
            case DrawCommand:
                await OfferDrawAsync(record, message.UserId, reply, ct);
                break;
            case DeclineDrawCommand:
                await DeclineDrawAsync(record, message.UserId, reply, ct);
                break;
            case AbortCommand:
                await AbortAsync(record, message.UserId, reply, ct);
                break;
            case BoardCommand:
                await ShowBoardAsync(record, reply, ct);
                break;
            case PgnCommand:
                await ShowPgnAsync(record, reply, ct);
                break;
        }
    }

    public async Task HandleActionAsync(IncomingAction action, CancellationToken ct = default)
    {
        var record = await games.GetAsync(action.ChannelId, action.ThreadTs, ct);
        if (record is null)
        {
            logger.LogWarning("Button {Action} clicked for unknown game {Channel}/{Thread}", action.ActionId, action.ChannelId, action.ThreadTs);
            return;
        }

        var reply = (string text) => slack.PostEphemeralAsync(action.ChannelId, action.UserId, record.ThreadTs, text, ct);
        switch (action.ActionId)
        {
            case ActionIds.Accept:
                await AcceptAsync(record, action.UserId, reply, ct);
                break;
            case ActionIds.Decline:
                await DeclineAsync(record, action.UserId, reply, ct);
                break;
            case ActionIds.Resign:
                await ResignAsync(record, action.UserId, reply, ct);
                break;
            case ActionIds.OfferDraw or ActionIds.AcceptDraw:
                await OfferDrawAsync(record, action.UserId, reply, ct);
                break;
            case ActionIds.DeclineDraw:
                await DeclineDrawAsync(record, action.UserId, reply, ct);
                break;
        }
    }

    /// <summary>Lets players type "e4" in the game thread without mentioning the bot.</summary>
    private async Task HandleUnaddressedThreadMessageAsync(IncomingMessage message, string botId, CancellationToken ct)
    {
        // Mentions arrive separately as app_mention events, so skip them here to avoid handling them twice.
        if (!Options.AllowMovesWithoutMention || message.ThreadTs is null || message.Text.Contains($"<@{botId}"))
        {
            return;
        }

        var text = message.Text.Trim();
        if (!Notation.LooksLikeMove(text))
        {
            return;
        }

        var record = await games.GetAsync(message.ChannelId, message.ThreadTs, ct);
        if (record is not { State: GameState.Active } || !record.IsPlayer(message.UserId))
        {
            return;
        }

        await MoveAsync(record, message.UserId, text, t => slack.PostEphemeralAsync(message.ChannelId, message.UserId, record.ThreadTs, t, ct), ct);
    }

    private async Task ChallengeAsync(IncomingMessage message, ChallengeCommand challenge, string botId, CancellationToken ct)
    {
        if (challenge.OpponentId == message.UserId)
        {
            await EphemeralAsync(message, "You can't challenge yourself — mention someone else.", ct);
            return;
        }

        if (challenge.OpponentId == botId)
        {
            await EphemeralAsync(message, "I'm just the referee. Challenge a human!", ct);
            return;
        }

        var challengerIsWhite = challenge.ChallengerColor switch
        {
            PieceColor.White => true,
            PieceColor.Black => false,
            _ => Random.Shared.Next(2) == 0,
        };
        var whiteId = challengerIsWhite ? message.UserId : challenge.OpponentId;
        var blackId = challengerIsWhite ? challenge.OpponentId : message.UserId;

        // The challenge is always a new top-level message so each game gets a thread of its own.
        var card = Messages.ChallengeCard(message.UserId, challenge.OpponentId, whiteId);
        var ts = await slack.PostMessageAsync(new SlackMessage(message.ChannelId, card.Text, card.Blocks), ct);

        var now = time.GetUtcNow();
        await games.SaveAsync(new GameRecord
        {
            ChannelId = message.ChannelId,
            ThreadTs = ts,
            ChallengerId = message.UserId,
            OpponentId = challenge.OpponentId,
            WhiteId = whiteId,
            BlackId = blackId,
            CreatedAt = now,
            UpdatedAt = now,
        }, ct);
    }

    private async Task AcceptAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (record.State != GameState.Challenged)
        {
            await reply("This challenge has already been answered.");
            return;
        }

        if (userId != record.OpponentId)
        {
            await reply(userId == record.ChallengerId
                ? $"Waiting for <@{record.OpponentId}> to accept."
                : $"Only <@{record.OpponentId}> can accept this challenge.");
            return;
        }

        record.State = GameState.Active;
        await SaveAsync(record, ct);

        var game = record.LoadGame();
        await UpdateCardAsync(record, game, ct);
        await PostBoardAsync(record, game, ct);
    }

    private async Task DeclineAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (record.State != GameState.Challenged)
        {
            await reply(record.State == GameState.Active
                ? "The game has already started. Use `@chessbot resign` or `@chessbot abort` instead."
                : "This challenge has already been answered.");
            return;
        }

        if (userId != record.OpponentId && userId != record.ChallengerId)
        {
            await reply("This isn't your challenge.");
            return;
        }

        record.State = GameState.Declined;

        // Abandoned marks a challenge withdrawn by the challenger rather than declined by the opponent.
        record.Termination = userId == record.ChallengerId ? Termination.Abandoned : Termination.None;
        await SaveAsync(record, ct);
        await UpdateCardAsync(record, null, ct);
    }

    private async Task MoveAsync(GameRecord record, string userId, string input, Func<string, Task> reply, CancellationToken ct)
    {
        if (!await EnsureActivePlayerAsync(record, userId, reply))
        {
            return;
        }

        var game = record.LoadGame();
        var color = record.ColorOf(userId);
        if (game.Current.SideToMove != color)
        {
            await reply($"It's not your turn — waiting on <@{record.PlayerFor(game.Current.SideToMove)}>.");
            return;
        }

        if (!game.TryPlay(input, out _, out var error))
        {
            await reply(error);
            return;
        }

        record.SyncFrom(game);
        record.DrawOfferedBy = null;
        await SaveAsync(record, ct);

        if (game.IsOver)
        {
            await FinishAsync(record, game, ct);
        }
        else
        {
            await PostBoardAsync(record, game, ct);
        }
    }

    private async Task ResignAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (!await EnsureActivePlayerAsync(record, userId, reply))
        {
            return;
        }

        var game = record.LoadGame();
        game.Resign(record.ColorOf(userId));
        record.SyncFrom(game);
        await SaveAsync(record, ct);
        await FinishAsync(record, game, ct);
    }

    private async Task OfferDrawAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (!await EnsureActivePlayerAsync(record, userId, reply))
        {
            return;
        }

        var opponent = record.OpponentOf(userId);
        if (record.DrawOfferedBy == opponent)
        {
            var game = record.LoadGame();
            game.AgreeDraw();
            record.SyncFrom(game);
            await SaveAsync(record, ct);
            await FinishAsync(record, game, ct);
            return;
        }

        if (record.DrawOfferedBy == userId)
        {
            await reply($"You've already offered a draw. Waiting on <@{opponent}>.");
            return;
        }

        record.DrawOfferedBy = userId;
        await SaveAsync(record, ct);
        var offer = Messages.DrawOffer(userId, opponent);
        await slack.PostMessageAsync(new SlackMessage(record.ChannelId, offer.Text, offer.Blocks, record.ThreadTs), ct);
    }

    private async Task DeclineDrawAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (!await EnsureActivePlayerAsync(record, userId, reply))
        {
            return;
        }

        if (record.DrawOfferedBy != record.OpponentOf(userId))
        {
            await reply("There's no draw offer for you to decline.");
            return;
        }

        record.DrawOfferedBy = null;
        await SaveAsync(record, ct);
        await slack.PostMessageAsync(new SlackMessage(record.ChannelId, $"<@{userId}> declined the draw. Play on!", ThreadTs: record.ThreadTs), ct);
    }

    private async Task AbortAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (!await EnsureActivePlayerAsync(record, userId, reply))
        {
            return;
        }

        if (record.Moves.Count >= 2)
        {
            await reply("Both players have moved, so the game can't be aborted. You can `resign` or offer a `draw`.");
            return;
        }

        var game = record.LoadGame();
        game.Abandon();
        record.SyncFrom(game);
        await SaveAsync(record, ct);
        await FinishAsync(record, game, ct);
    }

    private async Task ShowBoardAsync(GameRecord record, Func<string, Task> reply, CancellationToken ct)
    {
        var game = record.LoadGame();
        switch (record.State)
        {
            case GameState.Active:
                await PostBoardAsync(record, game, ct);
                break;
            case GameState.Finished:
                await PostGameOverAsync(record, game, broadcast: false, ct);
                break;
            default:
                await reply("The game hasn't started yet.");
                break;
        }
    }

    private async Task ShowPgnAsync(GameRecord record, Func<string, Task> reply, CancellationToken ct)
    {
        var game = record.LoadGame();
        if (game.Moves.Count == 0)
        {
            await reply("No moves have been played yet.");
            return;
        }

        var pgn = await BuildPgnAsync(record, game, ct);
        var content = Messages.Pgn(pgn, record.AnalysisUrl ?? LichessClient.AnalysisBoardUrl(game.MovesSan()));
        await slack.PostMessageAsync(new SlackMessage(record.ChannelId, content.Text, content.Blocks, record.ThreadTs), ct);
    }

    private async Task ListGamesAsync(IncomingMessage message, CancellationToken ct)
    {
        var open = await games.GetOpenGamesAsync(message.UserId, ct);
        if (open.Count == 0)
        {
            await EphemeralAsync(message, "You have no open games. Start one with `@chessbot challenge @someone`.", ct);
            return;
        }

        var sb = new StringBuilder("*Your open games*\n");
        foreach (var record in open)
        {
            var link = await slack.GetPermalinkAsync(record.ChannelId, record.ThreadTs, ct);
            var opponent = record.OpponentOf(message.UserId);
            var status = record.State == GameState.Challenged
                ? (record.OpponentId == message.UserId ? "waiting for you to accept" : "waiting for them to accept")
                : record.LoadGame().Current.SideToMove == record.ColorOf(message.UserId) ? "*your move*" : "their move";
            var label = link is null ? $"vs <@{opponent}>" : $"<{link}|vs <@{opponent}>>";
            sb.AppendLine($"• {label} — {status}");
        }

        await EphemeralAsync(message, sb.ToString(), ct);
    }

    private async Task StatsAsync(IncomingMessage message, string userId, CancellationToken ct)
    {
        var standing = (await StandingsAsync(ct)).FirstOrDefault(s => s.UserId == userId);
        var text = standing is null
            ? $"<@{userId}> hasn't finished any games yet."
            : $"<@{userId}>: *{standing.Wins}* wins, *{standing.Losses}* losses, *{standing.Draws}* draws ({standing.Games} games)";
        await slack.PostMessageAsync(new SlackMessage(message.ChannelId, text, ThreadTs: message.ThreadTs), ct);
    }

    private async Task LeaderboardAsync(IncomingMessage message, CancellationToken ct)
    {
        var standings = await StandingsAsync(ct);
        if (standings.Count == 0)
        {
            await slack.PostMessageAsync(new SlackMessage(message.ChannelId, "No games have been finished yet.", ThreadTs: message.ThreadTs), ct);
            return;
        }

        var sb = new StringBuilder(":trophy: *Leaderboard* (1 point per win, ½ per draw)\n");
        var rank = 1;
        foreach (var s in standings.Take(10))
        {
            sb.AppendLine($"{rank++}. <@{s.UserId}> — *{s.Points:0.#}* pts ({s.Wins}W {s.Losses}L {s.Draws}D)");
        }

        await slack.PostMessageAsync(new SlackMessage(message.ChannelId, sb.ToString(), ThreadTs: message.ThreadTs), ct);
    }

    private async Task<List<Standing>> StandingsAsync(CancellationToken ct)
    {
        var table = new Dictionary<string, Standing>();
        Standing For(string user) => table.TryGetValue(user, out var s) ? s : table[user] = new Standing(user);

        foreach (var record in await games.GetFinishedGamesAsync(ct))
        {
            if (record.Termination == Termination.Abandoned)
            {
                continue;
            }

            var white = For(record.WhiteId);
            var black = For(record.BlackId);
            switch (record.Outcome)
            {
                case GameOutcome.WhiteWins:
                    white.Wins++;
                    black.Losses++;
                    break;
                case GameOutcome.BlackWins:
                    black.Wins++;
                    white.Losses++;
                    break;
                case GameOutcome.Draw:
                    white.Draws++;
                    black.Draws++;
                    break;
            }
        }

        return table.Values.OrderByDescending(s => s.Points).ThenByDescending(s => s.Games).ToList();
    }

    private async Task<bool> EnsureActivePlayerAsync(GameRecord record, string userId, Func<string, Task> reply)
    {
        var problem = record.State switch
        {
            GameState.Challenged => $"The game hasn't started — <@{record.OpponentId}> still needs to accept.",
            GameState.Declined => "That challenge was declined.",
            GameState.Finished => "This game is over. Start a new one with `@chessbot challenge @someone`.",
            _ when !record.IsPlayer(userId) => "You're not playing in this game — but feel free to watch!",
            _ => null,
        };

        if (problem is not null)
        {
            await reply(problem);
            return false;
        }

        return true;
    }

    private async Task PostBoardAsync(GameRecord record, Game game, CancellationToken ct)
    {
        var board = Messages.Board(record, game, Options.PublicBaseUrl);
        await slack.PostMessageAsync(new SlackMessage(record.ChannelId, board.Text, board.Blocks, record.ThreadTs), ct);
    }

    private async Task FinishAsync(GameRecord record, Game game, CancellationToken ct)
    {
        if (Options.ImportFinishedGamesToLichess && game.Moves.Count > 0 && game.Termination != Termination.Abandoned)
        {
            record.AnalysisUrl = await analysis.ImportAsync(await BuildPgnAsync(record, game, ct), ct);
            if (record.AnalysisUrl is not null)
            {
                await SaveAsync(record, ct);
            }
        }

        await PostGameOverAsync(record, game, broadcast: true, ct);
        await UpdateCardAsync(record, game, ct);
    }

    private async Task PostGameOverAsync(GameRecord record, Game game, bool broadcast, CancellationToken ct)
    {
        var pgn = await BuildPgnAsync(record, game, ct);
        var url = record.AnalysisUrl ?? LichessClient.AnalysisBoardUrl(game.MovesSan());
        var content = Messages.GameOver(record, game, pgn, url, Options.PublicBaseUrl);
        await slack.PostMessageAsync(new SlackMessage(record.ChannelId, content.Text, content.Blocks, record.ThreadTs, broadcast), ct);
    }

    private async Task UpdateCardAsync(GameRecord record, Game? game, CancellationToken ct)
    {
        var card = Messages.StatusCard(record, game);
        await slack.UpdateMessageAsync(record.ChannelId, record.ThreadTs, card.Text, card.Blocks, ct);
    }

    private async Task<string> BuildPgnAsync(GameRecord record, Game game, CancellationToken ct)
    {
        var headers = new Dictionary<string, string>
        {
            ["Event"] = "Slack chess",
            ["Site"] = "Slack",
            ["Date"] = record.CreatedAt.ToString("yyyy.MM.dd"),
            ["White"] = await slack.GetUserNameAsync(record.WhiteId, ct),
            ["Black"] = await slack.GetUserNameAsync(record.BlackId, ct),
        };
        return game.ToPgn(headers);
    }

    private Task SaveAsync(GameRecord record, CancellationToken ct)
    {
        record.UpdatedAt = time.GetUtcNow();
        return games.SaveAsync(record, ct);
    }

    private Task EphemeralAsync(IncomingMessage message, string text, CancellationToken ct) =>
        slack.PostEphemeralAsync(message.ChannelId, message.UserId, message.ThreadTs, text, ct);

    private sealed class Standing(string userId)
    {
        public string UserId { get; } = userId;

        public int Wins { get; set; }

        public int Losses { get; set; }

        public int Draws { get; set; }

        public int Games => Wins + Losses + Draws;

        public double Points => Wins + Draws / 2.0;
    }
}
