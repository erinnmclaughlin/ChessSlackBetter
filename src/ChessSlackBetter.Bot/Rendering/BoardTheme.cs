using SkiaSharp;

namespace ChessSlackBetter.Bot.Rendering;

/// <summary>Square colours for the board image. Players pick one with the <c>theme</c> command.</summary>
public sealed record BoardTheme(string Name, SKColor Light, SKColor Dark)
{
    public static readonly BoardTheme Brown = new("brown", SKColor.Parse("#f0d9b5"), SKColor.Parse("#b58863"));

    public static readonly IReadOnlyList<BoardTheme> All =
    [
        Brown,
        new("blue", SKColor.Parse("#dee3e6"), SKColor.Parse("#8ca2ad")),
        new("green", SKColor.Parse("#ffffdd"), SKColor.Parse("#86a666")),
        new("purple", SKColor.Parse("#9f90b0"), SKColor.Parse("#7d4a8d")),
        new("grey", SKColor.Parse("#d9d9d9"), SKColor.Parse("#a3a3a3")),
    ];

    public static BoardTheme Default => Brown;

    // Last-move highlight: a translucent yellow-green laid over the square, as lichess does.
    private static readonly SKColor HighlightOverlay = new(155, 199, 0);
    private const float HighlightAlpha = 0.41f;

    public SKColor LastMoveLight => Highlight(Light);

    public SKColor LastMoveDark => Highlight(Dark);

    public static BoardTheme? Find(string? name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static SKColor Highlight(SKColor square)
    {
        static byte Mix(byte under, byte over) => (byte)Math.Round(under * (1 - HighlightAlpha) + over * HighlightAlpha);
        return new SKColor(
            Mix(square.Red, HighlightOverlay.Red),
            Mix(square.Green, HighlightOverlay.Green),
            Mix(square.Blue, HighlightOverlay.Blue));
    }
}
