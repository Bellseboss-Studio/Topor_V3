// Pure hole visual-state mapper. NO UnityEngine dependency — EditMode-testable.
// NOTE (gameplay): during Telegraph nobody reveals WHICH hole will spawn — only the
// threatened crop flashes. The hole only becomes visible (Emphasis) when the mole is
// Rising|Up, so the player reacts at spawn, not reading the telegraph.
// Priority: active flash (hit/miss until expiry) > phase states > Dim.
public enum HoleVisualState
{
    Dim,
    Emphasis,
    HitFlash,
    MissFlash,
}

public sealed class HoleVisuals
{
    private readonly float[] _flashUntilMs;
    private readonly bool[] _flashWasHit;
    private readonly float _flashDurationMs;

    public HoleVisuals(int holeCount, float flashDurationMs = 150f)
    {
        _flashUntilMs = new float[holeCount];
        _flashWasHit = new bool[holeCount];
        _flashDurationMs = flashDurationMs;
        for (int i = 0; i < holeCount; i++)
            _flashUntilMs[i] = float.NegativeInfinity;
    }

    public void RegisterTap(int holeIndex, bool wasHit, float nowMs)
    {
        if (holeIndex < 0 || holeIndex >= _flashUntilMs.Length) return;
        _flashUntilMs[holeIndex] = nowMs + _flashDurationMs;
        _flashWasHit[holeIndex] = wasHit;
    }

    public HoleVisualState StateFor(int holeIndex, MolePhase phase, float nowMs)
    {
        if (holeIndex < 0 || holeIndex >= _flashUntilMs.Length)
            return HoleVisualState.Dim;

        // Active flash takes priority over phase-driven states.
        if (nowMs < _flashUntilMs[holeIndex])
            return _flashWasHit[holeIndex] ? HoleVisualState.HitFlash : HoleVisualState.MissFlash;

        return PhaseToState(phase);
    }

    private static HoleVisualState PhaseToState(MolePhase phase)
    {
        switch (phase)
        {
            case MolePhase.Telegraphing:
                // Gameplay: do NOT reveal the spawn hole during the warning — the
                // hole stays dim; only the threatened crop telegraphs the risk.
                return HoleVisualState.Dim;
            case MolePhase.Rising:
            case MolePhase.Up:
                return HoleVisualState.Emphasis;
            default:
                return HoleVisualState.Dim;
        }
    }
}
