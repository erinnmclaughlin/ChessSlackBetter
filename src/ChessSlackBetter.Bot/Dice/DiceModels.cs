namespace ChessSlackBetter.Bot.Dice;

public static class DiceLimits
{
    /// <summary>Accepted (rolled) gambles each player gets per game. Cancelled or vetoed ones don't count.</summary>
    public const int RollsPerPlayer = 3;

    public const int VetoesPerPlayer = 1;
}

/// <summary>One change to the board, as described by the referee. Unused fields are empty strings.</summary>
/// <param name="Type">transform, remove, add or move.</param>
/// <param name="Square">The square affected (the source square for a move).</param>
/// <param name="ToSquare">Destination for a move.</param>
/// <param name="Piece">New piece type for transform/add: pawn, knight, bishop, rook or queen.</param>
/// <param name="Color">Colour for add (or to change sides in a transform): white or black.</param>
public sealed record DiceEffect(string Type, string Square, string ToSquare, string Piece, string Color);

/// <summary>The referee's reading of a gamble request.</summary>
public sealed record DiceRuling(
    bool Accepted,
    string Message,
    IReadOnlyList<DiceEffect> SuccessEffects,
    IReadOnlyList<DiceEffect> FailureEffects,
    string SuccessSummary,
    string FailureSummary,
    double SuccessPositionalBonus,
    double FailurePositionalBonus,
    string Reasoning);

/// <summary>A gamble waiting for the requester to roll or cancel (or the opponent to veto).</summary>
public sealed class DiceProposal
{
    public required string RequesterId { get; init; }

    public required string Request { get; init; }

    /// <summary>Square edits in text form, e.g. "e4=B".</summary>
    public required List<string> SuccessEdits { get; init; }

    public required List<string> FailureEdits { get; init; }

    public required string SuccessSummary { get; init; }

    public required string FailureSummary { get; init; }

    /// <summary>Minimum d20 roll needed to succeed.</summary>
    public required int Target { get; init; }

    public required string Reasoning { get; init; }

    /// <summary>The Slack message showing the proposal, so it can be updated with the outcome.</summary>
    public string? MessageTs { get; set; }
}

public sealed record DiceRollResult(string UserId, string Request, int Target, int Roll, bool Success, string Summary);

/// <summary>Everything a dice game tracks on top of the moves. Stored as JSON on the game.</summary>
public sealed class DiceState
{
    public Dictionary<string, int> RollsUsed { get; set; } = new();

    public List<string> VetoesUsed { get; set; } = [];

    public DiceProposal? Pending { get; set; }

    public List<DiceRollResult> Log { get; set; } = [];

    public int RollsLeft(string userId) => DiceLimits.RollsPerPlayer - RollsUsed.GetValueOrDefault(userId);

    public int VetoesLeft(string userId) => DiceLimits.VetoesPerPlayer - VetoesUsed.Count(u => u == userId);
}
