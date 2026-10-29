using System.Text.RegularExpressions;
using ChessSlackBetter.Chess;

namespace ChessSlackBetter.Bot.Games;

public abstract record BotCommand;

public sealed record HelpCommand : BotCommand;

/// <summary>Challenge another user. <see cref="ChallengerColor"/> is null for a random colour.</summary>
public sealed record ChallengeCommand(string OpponentId, PieceColor? ChallengerColor) : BotCommand;

public sealed record AcceptCommand : BotCommand;

public sealed record DeclineCommand : BotCommand;

public sealed record MoveCommand(string Move) : BotCommand;

public sealed record ResignCommand : BotCommand;

public sealed record DrawCommand : BotCommand;

public sealed record DeclineDrawCommand : BotCommand;

public sealed record AbortCommand : BotCommand;

public sealed record BoardCommand : BotCommand;

public sealed record PgnCommand : BotCommand;

public sealed record GamesCommand : BotCommand;

/// <summary>Win/loss record for a user; <see cref="UserId"/> is null for the person asking.</summary>
public sealed record StatsCommand(string? UserId) : BotCommand;

public sealed record LeaderboardCommand : BotCommand;

public sealed record UnknownCommand(string Text) : BotCommand;

public static partial class CommandParser
{
    [GeneratedRegex(@"<@([A-Z0-9]+)(?:\|[^>]*)?>")]
    private static partial Regex Mention();

    /// <summary>Parses the text of a message after the bot's own mention has been removed.</summary>
    public static BotCommand Parse(string text)
    {
        var trimmed = text.Trim();
        var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return new HelpCommand();
        }

        var verb = words[0].ToLowerInvariant().TrimEnd('!', '.', '?');
        var mentioned = Mention().Matches(trimmed).Select(m => m.Groups[1].Value).FirstOrDefault();

        switch (verb)
        {
            case "help" or "commands" or "?":
                return new HelpCommand();
            case "challenge" or "play" or "duel" or "vs":
                if (mentioned is null)
                {
                    return new UnknownCommand(trimmed);
                }

                PieceColor? color = null;
                if (words.Any(w => w.Equals("white", StringComparison.OrdinalIgnoreCase))) color = PieceColor.White;
                if (words.Any(w => w.Equals("black", StringComparison.OrdinalIgnoreCase))) color = PieceColor.Black;
                return new ChallengeCommand(mentioned, color);
            case "accept":
                return new AcceptCommand();
            case "decline" or "reject" or "cancel" or "withdraw":
                return words.Length > 1 && words[1].Equals("draw", StringComparison.OrdinalIgnoreCase)
                    ? new DeclineDrawCommand()
                    : new DeclineCommand();
            case "resign" or "forfeit" or "gg":
                return new ResignCommand();
            case "draw" or "offer":
                return new DrawCommand();
            case "abort":
                return new AbortCommand();
            case "board" or "show" or "status":
                return new BoardCommand();
            case "pgn" or "analyze" or "analysis":
                return new PgnCommand();
            case "games" or "list":
                return new GamesCommand();
            case "stats" or "record":
                return new StatsCommand(mentioned);
            case "leaderboard" or "top" or "rankings":
                return new LeaderboardCommand();
            case "move" or "m" when words.Length > 1:
                return new MoveCommand(words[1]);
        }

        if (words.Length == 1 && Notation.LooksLikeMove(words[0]))
        {
            return new MoveCommand(words[0]);
        }

        return new UnknownCommand(trimmed);
    }

    public static string StripMention(string text, string userId) =>
        Regex.Replace(text, $@"<@{Regex.Escape(userId)}(?:\|[^>]*)?>", "").Trim();
}
