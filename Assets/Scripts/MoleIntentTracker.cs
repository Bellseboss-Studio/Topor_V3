using System.Collections.Generic;

// Edge-driven phase → intent projector. Pure C#, EditMode-testable.
// Uses ONLY public GameRules API (GetPhase, GetMod).
//
// B-2 spec: emits ONLY phase-edge intents (Hide/Rise/Search/Sink/Reset).
// Hit and Escape are emitted at their real event sources (TryHit/DrainEscapes),
// NOT in the tracker — binding decisions D-HIT and D-ESC.
//
// B-3: Hide, Rise, Sink, Reset are one-shot (edge signal). Search is the
// ONLY state intent — emitted once at Rising→Up and NOT re-emitted per frame.
//
// Poll is idempotent: calling it twice at the same nowMs returns empty on the
// second call because all prevPhases already match current phases.

public sealed class MoleIntentTracker
{
    private readonly MolePhase[] _prevPhases;

    /// <summary>
    /// Creates a tracker for the given number of holes. All holes start as Sunk.
    /// </summary>
    public MoleIntentTracker(int holeCount)
    {
        _prevPhases = new MolePhase[holeCount];
        for (int i = 0; i < holeCount; i++)
            _prevPhases[i] = MolePhase.Sunk;
    }

    /// <summary>
    /// Resets all stored phases to Sunk. Call on StartMatch so the next Poll
    /// treats the current game state as fresh.
    /// </summary>
    public void Reset()
    {
        for (int i = 0; i < _prevPhases.Length; i++)
            _prevPhases[i] = MolePhase.Sunk;
    }

    /// <summary>
    /// Compares stored phases vs. current GameRules phases for every hole.
    /// Returns a list of MoleIntentEvent for every detected phase edge.
    /// Stores current phases for the next poll (idempotent guard).
    ///
    /// Phase-edge mapping (B-2):
    ///   Sunk→Telegraphing  → Hide
    ///   Sunk→Rising        → Rise (ninja)
    ///   Telegraphing→Rising → Rise
    ///   Rising→Up          → Search
    ///   Up→Sinking         → Sink
    ///   Rising→Sinking     → Sink
    ///   Sinking→Sunk       → Reset
    ///
    /// Hit and Escape are NOT emitted here (D-HIT/D-ESC).
    /// </summary>
    public List<MoleIntentEvent> Poll(GameRules rules, float nowMs)
    {
        var events = new List<MoleIntentEvent>();

        for (int i = 0; i < _prevPhases.Length; i++)
        {
            MolePhase prev = _prevPhases[i];
            MolePhase curr = rules.GetPhase(i);

            if (prev == curr) continue; // no edge — idempotent guard

            MoleIntent? intent = null;

            if (prev == MolePhase.Sunk && curr == MolePhase.Telegraphing)
                intent = MoleIntent.Hide;
            else if (prev == MolePhase.Sunk && curr == MolePhase.Rising)
                intent = MoleIntent.Rise;
            else if (prev == MolePhase.Telegraphing && curr == MolePhase.Rising)
                intent = MoleIntent.Rise;
            else if (prev == MolePhase.Rising && curr == MolePhase.Up)
                intent = MoleIntent.Search;
            else if (prev == MolePhase.Up && curr == MolePhase.Sinking)
                intent = MoleIntent.Sink;
            else if (prev == MolePhase.Rising && curr == MolePhase.Sinking)
                intent = MoleIntent.Sink;
            else if (prev == MolePhase.Sinking && curr == MolePhase.Sunk)
                intent = MoleIntent.Reset;

            // Store current phase for next poll (even for undefined transitions)
            _prevPhases[i] = curr;

            if (intent.HasValue)
            {
                var mod = rules.GetMod(i);
                events.Add(new MoleIntentEvent(
                    i,
                    intent.Value,
                    mod?.species,
                    nowMs
                ));
            }
        }

        return events;
    }
}
