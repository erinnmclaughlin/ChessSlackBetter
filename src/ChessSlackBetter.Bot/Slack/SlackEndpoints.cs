using System.Text.Json.Nodes;
using ChessSlackBetter.Bot.Games;
using Microsoft.Extensions.Caching.Memory;

namespace ChessSlackBetter.Bot.Slack;

public static class SlackEndpoints
{
    public static void MapSlackEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/slack/events", HandleEventAsync);
        app.MapPost("/slack/interactions", HandleInteractionAsync);
    }

    private static async Task<IResult> HandleEventAsync(
        HttpRequest request,
        SlackSignatureVerifier verifier,
        WorkQueue queue,
        IMemoryCache seen,
        ILogger<WorkQueue> logger)
    {
        var body = await ReadVerifiedBodyAsync(request, verifier);
        if (body is null)
        {
            return Results.Unauthorized();
        }

        var json = JsonNode.Parse(body);
        switch (json?["type"]?.GetValue<string>())
        {
            case "url_verification":
                return Results.Json(new { challenge = json["challenge"]?.GetValue<string>() });
            case "event_callback":
                break;
            default:
                return Results.Ok();
        }

        // Slack retries events it thinks we missed; only handle each one once.
        var eventId = json["event_id"]?.GetValue<string>();
        if (eventId is not null && !seen.TryGetValue(eventId, out _))
        {
            seen.Set(eventId, true, TimeSpan.FromMinutes(15));
            if (ParseMessage(json["event"]) is { } message)
            {
                queue.Enqueue((services, ct) => services.GetRequiredService<ChessBotService>().HandleMessageAsync(message, ct));
            }
        }
        else
        {
            logger.LogDebug("Ignoring duplicate event {EventId}", eventId);
        }

        return Results.Ok();
    }

    private static async Task<IResult> HandleInteractionAsync(HttpRequest request, SlackSignatureVerifier verifier, WorkQueue queue)
    {
        var body = await ReadVerifiedBodyAsync(request, verifier);
        if (body is null)
        {
            return Results.Unauthorized();
        }

        var form = System.Web.HttpUtility.ParseQueryString(body);
        var payload = JsonNode.Parse(form["payload"] ?? "{}");
        if (payload?["type"]?.GetValue<string>() != "block_actions")
        {
            return Results.Ok();
        }

        var userId = payload["user"]?["id"]?.GetValue<string>();
        var channelId = payload["channel"]?["id"]?.GetValue<string>();
        var message = payload["message"];

        // Buttons on the challenge card sit on the thread's parent; everything else is posted inside the thread.
        var threadTs = message?["thread_ts"]?.GetValue<string>() ?? payload["container"]?["message_ts"]?.GetValue<string>();
        if (userId is null || channelId is null || threadTs is null)
        {
            return Results.Ok();
        }

        foreach (var action in payload["actions"]?.AsArray() ?? [])
        {
            var actionId = action?["action_id"]?.GetValue<string>();
            if (actionId is null || !actionId.StartsWith("chess_", StringComparison.Ordinal) || actionId == "chess_link")
            {
                continue;
            }

            var incoming = new IncomingAction(actionId, userId, channelId, threadTs);
            queue.Enqueue((services, ct) => services.GetRequiredService<ChessBotService>().HandleActionAsync(incoming, ct));
        }

        return Results.Ok();
    }

    public static IncomingMessage? ParseMessage(JsonNode? evt)
    {
        var type = evt?["type"]?.GetValue<string>();
        if (evt is null || type is not ("app_mention" or "message"))
        {
            return null;
        }

        // Ignore edits, deletions, joins and other bots (including ourselves).
        if (evt["subtype"] is not null || evt["bot_id"] is not null)
        {
            return null;
        }

        var channel = evt["channel"]?.GetValue<string>();
        var user = evt["user"]?.GetValue<string>();
        var ts = evt["ts"]?.GetValue<string>();
        if (channel is null || user is null || ts is null)
        {
            return null;
        }

        return new IncomingMessage(
            channel,
            user,
            evt["text"]?.GetValue<string>() ?? "",
            ts,
            evt["thread_ts"]?.GetValue<string>(),
            IsMention: type == "app_mention");
    }

    private static async Task<string?> ReadVerifiedBodyAsync(HttpRequest request, SlackSignatureVerifier verifier)
    {
        using var reader = new StreamReader(request.Body);
        var body = await reader.ReadToEndAsync();
        var valid = verifier.IsValid(
            request.Headers["X-Slack-Request-Timestamp"],
            request.Headers["X-Slack-Signature"],
            body);
        return valid ? body : null;
    }
}
