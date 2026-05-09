using JumpzysVortex.Services;

namespace JumpzysVortex.AI;

/// <summary>
/// Thin compatibility shim — delegates to StateEngine.
/// Use StateEngine directly for new code.
/// </summary>
public class PredictionEngine
{
    private readonly StateEngine _engine = new();

    public (SystemState State, string Tip) Evaluate(
        PerformanceSnapshot snap,
        IReadOnlyList<PerformanceSnapshot> history)
        => _engine.Evaluate(snap, history);
}
