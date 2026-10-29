using System.Diagnostics.CodeAnalysis;
using ChessSlackBetter.Chess;

namespace ChessSlackBetter.Bot.Dice;

/// <summary>The deterministic half of the referee: turning effects into board edits and pricing the gamble.</summary>
public static class DiceRules
{
    /// <summary>Largest positional adjustment (in pawns) the referee may add on top of material.</summary>
    public const double MaxPositionalBonus = 3;

    public static int PieceValue(PieceType type) => type switch
    {
        PieceType.Pawn => 1,
        PieceType.Knight or PieceType.Bishop => 3,
        PieceType.Rook => 5,
        PieceType.Queen => 9,
        _ => 0,
    };

    /// <summary>Applies the effects in order to a copy of the board and returns the squares that changed.</summary>
    public static bool TryBuildEdits(
        Position position,
        IReadOnlyList<DiceEffect> effects,
        [NotNullWhen(true)] out List<SquareEdit>? edits,
        [NotNullWhen(false)] out string? error)
    {
        edits = null;
        var board = Enumerable.Range(0, 64).Select(sq => position[sq]).ToArray();

        foreach (var effect in effects)
        {
            if (!Square.TryParse(effect.Square, out var square))
            {
                error = $"`{effect.Square}` isn't a square.";
                return false;
            }

            var current = board[square];
            switch (effect.Type.ToLowerInvariant())
            {
                case "transform":
                    if (current.IsEmpty)
                    {
                        error = $"There's no piece on {effect.Square} to transform.";
                        return false;
                    }

                    if (!TryParsePiece(effect.Piece, out var newType, out error))
                    {
                        return false;
                    }

                    var color = TryParseColor(effect.Color, out var c) ? c : current.Color;
                    board[square] = new Piece(newType, color);
                    break;

                case "remove":
                    if (current.IsEmpty)
                    {
                        error = $"There's no piece on {effect.Square} to remove.";
                        return false;
                    }

                    board[square] = Piece.Empty;
                    break;

                case "add":
                    if (!current.IsEmpty)
                    {
                        error = $"{effect.Square} is already occupied.";
                        return false;
                    }

                    if (!TryParsePiece(effect.Piece, out var addType, out error))
                    {
                        return false;
                    }

                    if (!TryParseColor(effect.Color, out var addColor))
                    {
                        error = "An added piece needs a colour.";
                        return false;
                    }

                    board[square] = new Piece(addType, addColor);
                    break;

                case "move":
                    if (current.IsEmpty)
                    {
                        error = $"There's no piece on {effect.Square} to move.";
                        return false;
                    }

                    if (!Square.TryParse(effect.ToSquare, out var to))
                    {
                        error = $"`{effect.ToSquare}` isn't a square.";
                        return false;
                    }

                    board[to] = current;
                    board[square] = Piece.Empty;
                    break;

                default:
                    error = $"Unknown effect `{effect.Type}`.";
                    return false;
            }
        }

        edits = Enumerable.Range(0, 64)
            .Where(sq => board[sq] != position[sq])
            .Select(sq => new SquareEdit(sq, board[sq]))
            .ToList();
        error = null;
        return true;
    }

    /// <summary>Change in (own material − opponent material) for <paramref name="player"/>.</summary>
    public static int MaterialDelta(Position position, IEnumerable<SquareEdit> edits, PieceColor player)
    {
        var delta = 0;
        foreach (var edit in edits)
        {
            delta -= SignedValue(position[edit.Square], player);
            delta += SignedValue(edit.Piece, player);
        }

        return delta;
    }

    /// <summary>
    /// The minimum d20 roll that makes the gamble fair: succeeding with probability p is fair when
    /// p × gain = (1 − p) × loss, i.e. p = loss / (gain + loss).
    /// </summary>
    public static int FairTarget(double gain, double loss)
    {
        var p = loss / (gain + loss);
        return (int)Math.Clamp(Math.Round(21 - 20 * p, MidpointRounding.AwayFromZero), 2, 20);
    }

    public static int SuccessPercent(int target) => (21 - target) * 5;

    private static int SignedValue(Piece piece, PieceColor player) =>
        piece.IsEmpty ? 0 : piece.Color == player ? PieceValue(piece.Type) : -PieceValue(piece.Type);

    private static bool TryParsePiece(string text, out PieceType type, [NotNullWhen(false)] out string? error)
    {
        type = text.ToLowerInvariant() switch
        {
            "pawn" => PieceType.Pawn,
            "knight" => PieceType.Knight,
            "bishop" => PieceType.Bishop,
            "rook" => PieceType.Rook,
            "queen" => PieceType.Queen,
            _ => PieceType.None,
        };
        error = type == PieceType.None ? $"`{text}` isn't a piece dice can create." : null;
        return error is null;
    }

    private static bool TryParseColor(string text, out PieceColor color)
    {
        color = text.Equals("black", StringComparison.OrdinalIgnoreCase) ? PieceColor.Black : PieceColor.White;
        return text.Equals("white", StringComparison.OrdinalIgnoreCase) || text.Equals("black", StringComparison.OrdinalIgnoreCase);
    }
}
