using System.Text.Json.Nodes;

namespace ChessSlackBetter.Bot.Games;

public interface IAnalysisLinks
{
    /// <summary>Imports a finished game to lichess.org and returns its URL, or null if that failed.</summary>
    Task<string?> ImportAsync(string pgn, CancellationToken ct = default);
}

public sealed class LichessClient(HttpClient http, ILogger<LichessClient> logger) : IAnalysisLinks
{
    public async Task<string?> ImportAsync(string pgn, CancellationToken ct = default)
    {
        try
        {
            using var content = new FormUrlEncodedContent([new("pgn", pgn)]);
            using var response = await http.PostAsync("https://lichess.org/api/import", content, ct);
            response.EnsureSuccessStatusCode();
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            return json?["url"]?.GetValue<string>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "Lichess import failed");
            return null;
        }
    }

    /// <summary>A lichess analysis board for the moves so far; works without importing anything.</summary>
    public static string AnalysisBoardUrl(string sanMoves) =>
        "https://lichess.org/analysis/pgn/" + Uri.EscapeDataString(sanMoves);
}
