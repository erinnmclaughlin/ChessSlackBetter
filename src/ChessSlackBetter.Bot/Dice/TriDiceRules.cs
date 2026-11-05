using System.Diagnostics.CodeAnalysis;
using ChessSlackBetter.Chess;
using ChessSlackBetter.Chess.ThreePlayer;

namespace ChessSlackBetter.Bot.Dice;

/// <summary>The deterministic half of the referee for three-player dice games.</summary>
public static class TriDiceRules
{
    /// <summary>Applies the effects in order to a copy of the board and returns the squares that changed.</summary>
    public static bool TryBuildEdits(
        TriPosition position,
        IReadOnlyList<DiceEffect> effects,
        [NotNullWhen(true)] out List<TriSquareEdit>? edits,
        [NotNullWhen(false)] out string? error)
    {
        edits = null;
        var board = Enumerable.Range(0, TriSquare.Count).Select(sq => position[sq]).ToArray();

        foreach (var effect in effects)
        {
            if (!TriSquare.TryParse(effect.Square, out var square))
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

                    board[square] = new TriPiece(newType, TryParseColor(effect.Color, out var c) ? c : current.Color);
                    break;

                case "remove":
                    if (current.IsEmpty)
                    {
                        error = $"There's no piece on {effect.Square} to remove.";
                        return false;
                    }

                    board[square] = TriPiece.Empty;
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

                    board[square] = new TriPiece(addType, addColor);
                    break;

                case "move":
                    if (current.IsEmpty)
                    {
                        error = $"There's no piece on {effect.Square} to move.";
                        return false;
                    }

                    if (!TriSquare.TryParse(effect.ToSquare, out var to))
                    {
                        error = $"`{effect.ToSquare}` isn't a square.";
                        return false;
                    }

                    board[to] = current;
                    board[square] = TriPiece.Empty;
                    break;

                default:
                    error = $"Unknown effect `{effect.Type}`.";
                    return false;
            }
        }

        edits = Enumerable.Range(0, TriSquare.Count)
            .Where(sq => board[sq] != position[sq])
            .Select(sq => new TriSquareEdit(sq, board[sq]))
            .ToList();
        error = null;
        return true;
    }

    /// <summary>
    /// How much the edits help <paramref name="player"/>: their own change in material, minus the average change for
    /// the opponents still playing. With two opponents, costing one of them a piece also helps the other, so it's
    /// worth half as much as gaining that piece yourself. With one opponent left this is the usual two-player sum.
    /// </summary>
    public static double MaterialDelta(TriPosition position, IEnumerable<TriSquareEdit> edits, TriColor player)
    {
        var change = TriColors.All.ToDictionary(c => c, _ => 0);
        foreach (var edit in edits)
        {
            var before = position[edit.Square];
            if (!before.IsEmpty)
            {
                change[before.Color] -= DiceRules.PieceValue(before.Type);
            }

            if (!edit.Piece.IsEmpty)
            {
                change[edit.Piece.Color] += DiceRules.PieceValue(edit.Piece.Type);
            }
        }

        var opponents = position.AliveColors.Where(c => c != player).ToList();
        return change[player] - (opponents.Count == 0 ? 0 : opponents.Average(c => change[c]));
    }

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

    private static bool TryParseColor(string text, out TriColor color)
    {
        TriColor? parsed = text.ToLowerInvariant() switch
        {
            "white" => TriColor.White,
            "black" => TriColor.Black,
            "red" => TriColor.Red,
            _ => null,
        };
        color = parsed ?? TriColor.White;
        return parsed is not null;
    }
}
