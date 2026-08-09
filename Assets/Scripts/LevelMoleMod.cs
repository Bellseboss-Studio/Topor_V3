using System;

[Serializable]
public class LevelMoleMod
{
    public MoleSpecies species;
    public float speedMultiplier = 1f;
    public int bonusHits = 0;
    public float telegraphOverrideMs = -1f;
    public int weight = 1;

    public int TotalHits => species != null ? species.baseHitsToKill + bonusHits : 1 + bonusHits;
    public float EffectiveSpeed => species != null ? species.baseSpeedMultiplier * speedMultiplier : speedMultiplier;
    public float EffectiveTelegraphMs =>
        telegraphOverrideMs >= 0f ? telegraphOverrideMs :
        (species != null ? species.baseTelegraphDurationMs : 800f);
    public bool UseTelegraph => species != null && species.useTelegraph;
    public int BitesOnEscape => species != null ? species.bitesOnEscape : 1;
    public bool IgnoresDecoy => species != null && species.ignoresDecoy;
}
