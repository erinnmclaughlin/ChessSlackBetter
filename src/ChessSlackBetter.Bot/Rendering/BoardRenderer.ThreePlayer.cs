using System.Text;
using ChessSlackBetter.Chess;
using ChessSlackBetter.Chess.ThreePlayer;
using SkiaSharp;

namespace ChessSlackBetter.Bot.Rendering;

/// <summary>
/// The three-player board, drawn as a triangle with its corners cut off. Each player's back rank lies along one
/// long side and the cut corners carry the files that join two halves (a–d, e–h and i–l). Each half is two
/// four-by-four quadrants stretched between its back rank, the middle of its side and the centre.
/// </summary>
public sealed partial class BoardRenderer
{
    public const int ThreePlayerWidth = 760;
    public const int ThreePlayerHeight = 690;

    /// <summary>How much of each triangle side is cut off at each corner.</summary>
    private const float CornerCut = 0.25f;

    private static readonly SKColor RedTint = SKColor.Parse("#c0392b");
    private static readonly SKColor Background = SKColor.Parse("#302e2b");
    private static readonly SKColor LabelColor = SKColor.Parse("#e8e6e3");

    private static readonly Dictionary<TriColor, SKColor> SideColors = new()
    {
        [TriColor.White] = SKColor.Parse("#f5f5f5"),
        [TriColor.Black] = SKColor.Parse("#1a1a1a"),
        [TriColor.Red] = RedTint,
    };

    /// <param name="position">The position to draw.</param>
    /// <param name="highlights">Squares to highlight, usually the last move's from and to.</param>
    /// <param name="perspective">The side whose back rank is along the bottom.</param>
    public byte[] RenderThreePlayerPng(TriPosition position, IReadOnlyCollection<int> highlights, TriColor perspective)
    {
        using var surface = SKSurface.Create(new SKImageInfo(ThreePlayerWidth, ThreePlayerHeight));
        var canvas = surface.Canvas;
        canvas.Clear(Background);

        var layout = new TriangleLayout(perspective);
        using var fill = new SKPaint { IsAntialias = true };
        using var edge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1, Color = Dark.WithAlpha(120) };
        using var font = new SKFont(SKTypeface.Default, 13) { Embolden = true };
        using var label = new SKPaint { IsAntialias = true, Color = LabelColor };
        using var redPieces = new SKPaint { ColorFilter = RedFilter() };

        var checkedKings = TriColors.All.Where(position.IsInCheck).Select(position.FindKing).ToHashSet();

        for (var sq = 0; sq < TriSquare.Count; sq++)
        {
            var corners = layout.Corners(sq);
            using var path = Polygon(corners);
            var light = TriSquare.IsLight(sq);
            fill.Color = highlights.Contains(sq) ? (light ? LastMoveLight : LastMoveDark) : (light ? Light : Dark);
            canvas.DrawPath(path, fill);
            canvas.DrawPath(path, edge);

            var centre = new SKPoint(corners.Average(p => p.X), corners.Average(p => p.Y));
            var size = (float)Math.Sqrt(Area(corners));
            if (checkedKings.Contains(sq))
            {
                using var glow = SKShader.CreateRadialGradient(
                    centre,
                    size * 0.6f,
                    [new SKColor(255, 0, 0), new SKColor(231, 0, 0), new SKColor(169, 0, 0, 0)],
                    [0f, 0.25f, 1f],
                    SKShaderTileMode.Clamp);
                fill.Shader = glow;
                canvas.Save();
                canvas.ClipPath(path, antialias: true);
                canvas.DrawPath(path, fill);
                canvas.Restore();
                fill.Shader = null;
            }

            DrawThreePlayerLabels(canvas, font, label, layout, sq, corners);

            var piece = position[sq];
            if (!piece.IsEmpty && _pieces[new Piece(piece.Type, piece.Color == TriColor.White ? PieceColor.White : PieceColor.Black)].Picture is { } picture)
            {
                var pieceSize = size * 0.92f;
                var scale = pieceSize / picture.CullRect.Width;
                var matrix = SKMatrix.CreateScale(scale, scale)
                    .PostConcat(SKMatrix.CreateTranslation(centre.X - pieceSize / 2, centre.Y - pieceSize / 2));
                canvas.DrawPicture(picture, in matrix, piece.Color == TriColor.Red ? redPieces : null);
            }
        }

        // A band along each back rank in that side's colour, thicker for the side to move.
        foreach (var color in TriColors.All)
        {
            if (!position.IsAlive(color))
            {
                continue;
            }

            var (from, to) = layout.BackRank(color);
            using var band = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = color == position.SideToMove ? 7 : 3,
                Color = SideColors[color],
            };
            var middle = Midpoint(from, to);
            var shift = layout.Outward(middle, 30) - middle;
            canvas.DrawLine(from + shift, to + shift, band);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Monospace board, one half at a time, for when no public URL is configured for images.</summary>
    public static string RenderThreePlayerText(TriPosition position)
    {
        var sb = new StringBuilder();
        foreach (var color in TriColors.All)
        {
            sb.AppendLine($"{color} half{(position.SideToMove == color ? " (to move)" : "")}:");
            for (var rank = 0; rank < 4; rank++)
            {
                var first = TriSquare.At(color, 0, rank);
                sb.Append(TriSquare.Name(first)[1..].PadLeft(3)).Append(' ');
                for (var file = 0; file < 8; file++)
                {
                    var piece = position[TriSquare.At(color, file, rank)];
                    sb.Append(piece.IsEmpty ? " · " : $"{piece.ToCode()} ");
                }

                sb.AppendLine();
            }

            sb.Append("    ").AppendLine(string.Join("", Enumerable.Range(0, 8).Select(f => $" {TriSquare.Name(TriSquare.At(color, f, 0))[0]} ")));
        }

        return sb.ToString().TrimEnd();
    }

    private static void DrawThreePlayerLabels(SKCanvas canvas, SKFont font, SKPaint paint, TriangleLayout layout, int sq, SKPoint[] corners)
    {
        var name = TriSquare.Name(sq);
        var file = TriSquare.File(sq);

        // Corners run back-left, back-right, front-right, front-left in the half's own frame.
        if (TriSquare.Rank(sq) == 0)
        {
            DrawOutside(name[..1], Midpoint(corners[0], corners[1]));
        }

        if (file == 0)
        {
            DrawOutside(name[1..], Midpoint(corners[0], corners[3]));
        }
        else if (file == 7)
        {
            DrawOutside(name[1..], Midpoint(corners[1], corners[2]));
        }

        void DrawOutside(string text, SKPoint edgeMiddle)
        {
            var at = layout.Outward(edgeMiddle, 12);
            canvas.DrawText(text, at.X, at.Y + 5, SKTextAlign.Center, font, paint);
        }
    }

    /// <summary>Darkens black pieces to red while leaving their white details alone.</summary>
    private static SKColorFilter RedFilter()
    {
        float r = RedTint.Red / 255f, g = RedTint.Green / 255f, b = RedTint.Blue / 255f;
        return SKColorFilter.CreateColorMatrix(
        [
            1 - r, 0, 0, 0, r,
            0, 1 - g, 0, 0, g,
            0, 0, 1 - b, 0, b,
            0, 0, 0, 1, 0,
        ]);
    }

    private static SKPath Polygon(SKPoint[] points)
    {
        using var builder = new SKPathBuilder();
        builder.AddPoly(points, close: true);
        return builder.Detach();
    }

    private static double Area(SKPoint[] p)
    {
        double sum = 0;
        for (var i = 0; i < p.Length; i++)
        {
            var next = p[(i + 1) % p.Length];
            sum += p[i].X * next.Y - next.X * p[i].Y;
        }

        return Math.Abs(sum) / 2;
    }

    private static SKPoint Midpoint(SKPoint a, SKPoint b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    /// <summary>Where every square goes, with <c>perspective</c>'s back rank along the bottom.</summary>
    private sealed class TriangleLayout
    {
        private readonly TriColor _perspective;
        private readonly SKPoint _centre;
        private readonly SKPoint[] _hexagon;

        public TriangleLayout(TriColor perspective)
        {
            _perspective = perspective;

            // An equilateral triangle with a flat bottom, in screen coordinates (y down), centred on its centroid.
            const float side = 860f;
            var height = side * MathF.Sqrt(3) / 2;
            var top = new SKPoint(0, -height * 2 / 3);
            var bottomLeft = new SKPoint(-side / 2, height / 3);
            var bottomRight = new SKPoint(side / 2, height / 3);

            // Going round clockwise on screen, each side's back rank runs from its own left to its own right.
            SKPoint Along(SKPoint a, SKPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            var hexagon = new[]
            {
                Along(bottomLeft, bottomRight, CornerCut), Along(bottomLeft, bottomRight, 1 - CornerCut),
                Along(bottomRight, top, CornerCut), Along(bottomRight, top, 1 - CornerCut),
                Along(top, bottomLeft, CornerCut), Along(top, bottomLeft, 1 - CornerCut),
            };

            // Fit the cut triangle (plus room for labels) in the image.
            var minX = hexagon.Min(p => p.X);
            var maxX = hexagon.Max(p => p.X);
            var minY = hexagon.Min(p => p.Y);
            var maxY = hexagon.Max(p => p.Y);
            var scale = Math.Min((ThreePlayerWidth - 90) / (maxX - minX), (ThreePlayerHeight - 90) / (maxY - minY));
            var offset = new SKPoint(ThreePlayerWidth / 2f - (minX + maxX) / 2 * scale, ThreePlayerHeight / 2f - (minY + maxY) / 2 * scale);
            _hexagon = hexagon.Select(p => new SKPoint(p.X * scale + offset.X, p.Y * scale + offset.Y)).ToArray();
            _centre = offset;
        }

        /// <summary>The four corners of a square: back-left, back-right, front-right, front-left in its half's frame.</summary>
        public SKPoint[] Corners(int square)
        {
            var file = TriSquare.File(square);
            var rank = TriSquare.Rank(square);
            var (leftEnd, rightEnd, leftCut, rightCut, middle) = Half(TriSquare.Half(square));

            // Bilinear patch for the quadrant: u across the files, v from the back rank towards the centre.
            Func<float, float, SKPoint> at = file < 4
                ? (u, v) => Blend(leftEnd, middle, _centre, leftCut, u, v)
                : (u, v) => Blend(middle, rightEnd, rightCut, _centre, u, v);
            var u0 = (file % 4) / 4f;
            var u1 = (file % 4 + 1) / 4f;
            var v0 = rank / 4f;
            var v1 = (rank + 1) / 4f;
            return [at(u0, v0), at(u1, v0), at(u1, v1), at(u0, v1)];
        }

        public (SKPoint From, SKPoint To) BackRank(TriColor color)
        {
            var (leftEnd, rightEnd, _, _, _) = Half(color);
            return (leftEnd, rightEnd);
        }

        /// <summary>A point moved away from the centre of the board.</summary>
        public SKPoint Outward(SKPoint point, float distance)
        {
            var dx = point.X - _centre.X;
            var dy = point.Y - _centre.Y;
            var length = MathF.Sqrt(dx * dx + dy * dy);
            return length == 0 ? point : new SKPoint(point.X + dx / length * distance, point.Y + dy / length * distance);
        }

        /// <summary>
        /// The perspective side sits along the bottom; the next side in turn order sits to its left (it shares
        /// the perspective side's left quadrant) and the previous side to its right.
        /// </summary>
        private (SKPoint LeftEnd, SKPoint RightEnd, SKPoint LeftCut, SKPoint RightCut, SKPoint Middle) Half(TriColor color)
        {
            var seat = color == _perspective ? 0 : color == _perspective.Previous() ? 1 : 2;
            var leftEnd = _hexagon[2 * seat];
            var rightEnd = _hexagon[2 * seat + 1];
            var leftCut = Midpoint(_hexagon[(2 * seat + 5) % 6], leftEnd);
            var rightCut = Midpoint(rightEnd, _hexagon[(2 * seat + 2) % 6]);
            return (leftEnd, rightEnd, leftCut, rightCut, Midpoint(leftEnd, rightEnd));
        }

        private static SKPoint Blend(SKPoint p00, SKPoint p10, SKPoint p11, SKPoint p01, float u, float v) => new(
            (1 - u) * (1 - v) * p00.X + u * (1 - v) * p10.X + u * v * p11.X + (1 - u) * v * p01.X,
            (1 - u) * (1 - v) * p00.Y + u * (1 - v) * p10.Y + u * v * p11.Y + (1 - u) * v * p01.Y);
    }
}
