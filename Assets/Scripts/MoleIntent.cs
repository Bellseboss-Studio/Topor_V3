// Pure C# intent vocabulary for the mole animation bridge.
// NO UnityEngine — only MoleSpecies (ScriptableObject) appears as an opaque nullable reference.
//
// B-1 spec: MoleIntent enum + MoleIntentEvent readonly struct carry
// HoleIndex, Intent, Species (nullable), and AtMs (rules clock).

public enum MoleIntent
{
    Hide,    // Sunk→Telegraphing — mole is about to appear
    Rise,    // *→Rising — mole emerges from hole
    Search,  // Rising→Up — mole is fully visible, idle
    Sink,    // *→Sinking — mole retreats into hole
    Hit,     // Emitted at TryHit event source (NOT tracker)
    Escape,  // Emitted at DrainEscapes event source (NOT tracker)
    Reset,   // Sinking→Sunk — mole fully hidden, cancel any juices
}

public readonly struct MoleIntentEvent
{
    public readonly int HoleIndex;
    public readonly MoleIntent Intent;
    public readonly MoleSpecies Species; // null when no species available at spawn
    public readonly float AtMs;

    public MoleIntentEvent(int holeIndex, MoleIntent intent, MoleSpecies species, float atMs)
    {
        HoleIndex = holeIndex;
        Intent = intent;
        Species = species;
        AtMs = atMs;
    }
}
