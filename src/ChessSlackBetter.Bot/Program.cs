using ChessSlackBetter.Bot;
using ChessSlackBetter.Bot.Games;
using ChessSlackBetter.Bot.Rendering;
using ChessSlackBetter.Bot.Slack;
using ChessSlackBetter.Chess;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SlackOptions>(builder.Configuration.GetSection(SlackOptions.Section));
builder.Services.Configure<ChessBotOptions>(builder.Configuration.GetSection(ChessBotOptions.Section));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SlackSignatureVerifier>();
builder.Services.AddHttpClient<ISlackApi, SlackApiClient>();
builder.Services.AddHttpClient<IAnalysisLinks, LichessClient>(http => http.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IGameRepository, SqliteGameRepository>();
builder.Services.AddSingleton<BoardRenderer>();
builder.Services.AddScoped<ChessBotService>();
builder.Services.AddSingleton<WorkQueue>();
builder.Services.AddHostedService<WorkQueueService>();

var app = builder.Build();

app.MapGet("/", () => "ChessSlackBetter is running.");
app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapSlackEndpoints();

// Slack fetches board images from here. The URL encodes the whole position, so responses can be cached forever.
app.MapGet("/board.png", (string fen, string? last, string? pov, BoardRenderer renderer, HttpResponse response) =>
{
    Position position;
    try
    {
        position = Position.FromFen(fen);
    }
    catch (FormatException)
    {
        return Results.BadRequest("Invalid FEN.");
    }

    Move? lastMove = null;
    if (last is { Length: 4 or 5 } &&
        Square.TryParse(last.AsSpan(0, 2), out var from) &&
        Square.TryParse(last.AsSpan(2, 2), out var to))
    {
        lastMove = new Move(from, to);
    }

    var perspective = pov == "b" ? PieceColor.Black : PieceColor.White;
    response.Headers.CacheControl = "public, max-age=31536000, immutable";
    return Results.File(renderer.RenderPng(position, lastMove, perspective), "image/png");
});

app.Run();

public partial class Program;
