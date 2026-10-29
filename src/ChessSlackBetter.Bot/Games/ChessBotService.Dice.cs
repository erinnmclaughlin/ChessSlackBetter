using ChessSlackBetter.Bot.Dice;
using ChessSlackBetter.Bot.Slack;
using ChessSlackBetter.Chess;

namespace ChessSlackBetter.Bot.Games;

/// <summary>ChessDiceRoll: on their turn a player may gamble on a d20 instead of moving.</summary>
public sealed partial class ChessBotService
{
    private async Task RequestRollAsync(GameRecord record, string userId, string request, Func<string, Task> reply, CancellationToken ct)
    {
        if (record.Mode != GameMode.DiceRoll)
        {
            await reply("Gambles are only for ChessDiceRoll games. Start one with `@ChessBetterBot diceroll @someone`.");
            return;
        }

        if (!await EnsureActivePlayerAsync(record, userId, reply))
        {
            return;
        }

        var game = record.LoadGame();
        if (game.Current.SideToMove != record.ColorOf(userId))
        {
            await reply($"You can only gamble on your own turn — waiting on <@{record.PlayerFor(game.Current.SideToMove)}>.");
            return;
        }

        if (record.Dice.Pending is not null)
        {
            await reply("You already have a gamble waiting. Roll or cancel it first.");
            return;
        }

        if (record.Dice.RollsLeft(userId) <= 0)
        {
            await reply($"You've used all {DiceLimits.RollsPerPlayer} of your rolls this game. Time to play it straight!");
            return;
        }

        DiceRuling ruling;
        try
        {
            ruling = await judge.JudgeAsync(game.Current, record.ColorOf(userId), request, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Dice referee failed for {Request}", request);
            await reply("The dice referee is unavailable right now, so I can't price that gamble. Make a normal move or try again later.");
            return;
        }

        if (!ruling.Accepted)
        {
            await reply($":game_die: {ruling.Message}");
            return;
        }

        if (!TryPrepareOutcome(game.Current, ruling.SuccessEffects, "If you win", out var successEdits, out var error) ||
            !TryPrepareOutcome(game.Current, ruling.FailureEffects, "If you lose", out var failureEdits, out error))
        {
            await reply($":game_die: I can't set up that gamble. {error}");
            return;
        }

        var player = record.ColorOf(userId);
        var gain = DiceRules.MaterialDelta(game.Current, successEdits, player) + ClampBonus(ruling.SuccessPositionalBonus);
        var loss = -(DiceRules.MaterialDelta(game.Current, failureEdits, player) + ClampBonus(ruling.FailurePositionalBonus));
        if (gain <= 0)
        {
            await reply(":game_die: Winning that gamble wouldn't actually help you. Ask for something that improves your position.");
            return;
        }

        if (loss <= 0)
        {
            await reply(":game_die: Losing has to cost you something. Add a real penalty, e.g. `…and if I fail I lose my knight`.");
            return;
        }

        var proposal = new DiceProposal
        {
            RequesterId = userId,
            Request = request,
            SuccessEdits = successEdits.Select(e => e.ToString()).ToList(),
            FailureEdits = failureEdits.Select(e => e.ToString()).ToList(),
            SuccessSummary = ruling.SuccessSummary,
            FailureSummary = ruling.FailureSummary,
            Target = DiceRules.FairTarget(gain, loss),
            Reasoning = ruling.Reasoning,
        };

        record.Dice.Pending = proposal;
        var content = Messages.DiceProposal(record, proposal);
        proposal.MessageTs = await slack.PostMessageAsync(new SlackMessage(record.ChannelId, content.Text, content.Blocks, record.ThreadTs), ct);
        await SaveAsync(record, ct);
    }

    private async Task ConfirmRollAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (record.Dice.Pending is not { } proposal)
        {
            await reply("There's no gamble waiting to be rolled.");
            return;
        }

        if (proposal.RequesterId != userId)
        {
            await reply($"Only <@{proposal.RequesterId}> can roll their gamble.");
            return;
        }

        if (!await EnsureActivePlayerAsync(record, userId, reply))
        {
            return;
        }

        var game = record.LoadGame();
        var roll = dice.RollD20();
        var success = roll >= proposal.Target;
        var edits = (success ? proposal.SuccessEdits : proposal.FailureEdits).Select(SquareEdit.Parse).ToList();

        record.Dice.Pending = null;
        if (!game.TryApplyEdits(edits, out var error))
        {
            // The board can't have changed since the proposal, so this shouldn't happen; don't charge a roll for it.
            logger.LogError("Approved gamble could not be applied: {Error}", error);
            await SaveAsync(record, ct);
            await CloseProposalAsync(record, proposal, "_Something went wrong applying this gamble, so it was called off. No roll used._", ct);
            return;
        }

        var outcome = Messages.DiceRollOutcome(proposal, roll, success);
        record.Dice.RollsUsed[userId] = record.Dice.RollsUsed.GetValueOrDefault(userId) + 1;
        record.Dice.Log.Add(new DiceRollResult(userId, proposal.Request, proposal.Target, roll, success,
            success ? proposal.SuccessSummary : proposal.FailureSummary));
        record.SyncFrom(game);
        record.DrawOfferedBy = null;
        await SaveAsync(record, ct);

        await CloseProposalAsync(record, proposal, outcome, ct);
        await slack.PostMessageAsync(new SlackMessage(record.ChannelId, $"<@{userId}> {outcome}", ThreadTs: record.ThreadTs), ct);

        if (game.IsOver)
        {
            await FinishAsync(record, game, ct);
        }
        else
        {
            await PostBoardAsync(record, game, ct);
        }
    }

    private async Task CancelRollAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (record.Dice.Pending is not { } proposal)
        {
            await reply("There's no gamble to cancel.");
            return;
        }

        if (proposal.RequesterId != userId)
        {
            await reply(record.IsPlayer(userId)
                ? "Only the player who proposed the gamble can cancel it. You can veto it instead."
                : $"Only <@{proposal.RequesterId}> can cancel their gamble.");
            return;
        }

        record.Dice.Pending = null;
        await SaveAsync(record, ct);
        await CloseProposalAsync(record, proposal, $"_Cancelled by <@{userId}>. No roll used — still their move._", ct);
    }

    private async Task VetoAsync(GameRecord record, string userId, Func<string, Task> reply, CancellationToken ct)
    {
        if (record.Dice.Pending is not { } proposal)
        {
            await reply("There's no gamble to veto.");
            return;
        }

        if (proposal.RequesterId == userId)
        {
            await reply("You can't veto your own gamble. Cancel it instead.");
            return;
        }

        if (!record.IsPlayer(userId))
        {
            await reply("Only the players can veto a gamble.");
            return;
        }

        if (record.Dice.VetoesLeft(userId) <= 0)
        {
            await reply("You've already used your veto this game.");
            return;
        }

        record.Dice.Pending = null;
        record.Dice.VetoesUsed.Add(userId);
        await SaveAsync(record, ct);
        await CloseProposalAsync(record, proposal,
            $":no_entry: *Vetoed* by <@{userId}>. No roll used — <@{proposal.RequesterId}>, it's still your move.", ct);
    }

    private async Task CloseProposalAsync(GameRecord record, DiceProposal proposal, string outcome, CancellationToken ct)
    {
        if (proposal.MessageTs is null)
        {
            return;
        }

        var closed = Messages.DiceProposalClosed(proposal, outcome);
        await slack.UpdateMessageAsync(record.ChannelId, proposal.MessageTs, closed.Text, closed.Blocks, ct);
    }

    private static bool TryPrepareOutcome(
        Position position,
        IReadOnlyList<DiceEffect> effects,
        string label,
        out List<SquareEdit> edits,
        out string? error)
    {
        edits = [];
        if (!DiceRules.TryBuildEdits(position, effects, out var built, out error))
        {
            error = $"{label}: {error}";
            return false;
        }

        if (!Game.ValidateEdits(position, built, out error))
        {
            error = $"{label}: {error}";
            return false;
        }

        edits = built;
        return true;
    }

    private static double ClampBonus(double bonus) =>
        Math.Clamp(bonus, -DiceRules.MaxPositionalBonus, DiceRules.MaxPositionalBonus);
}
