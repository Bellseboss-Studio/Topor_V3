using System;

[Serializable]
public class LevelProfile
{
    public string displayName;
    public string description;
    public float durationMs = 60_000f;
    public float spawnIntervalMs = 3_000f;
    public float upWindowMs = 1_600f;
    public float riseDurationMs = 250f;
    public float sinkDurationMs = 250f;
    public int maxConcurrentMoles = 2;
    public float intensityStart = 1f;
    public float intensityEnd = 1f;
    public int seedsBase = 0;

    public LevelMoleMod[] moleMods = Array.Empty<LevelMoleMod>();
    public LevelCropConfig[] cropConfigs = Array.Empty<LevelCropConfig>();

    public float IntensityAt(float progress01)
    {
        float p = Math.Clamp(progress01, 0f, 1f);
        return intensityStart + (intensityEnd - intensityStart) * p;
    }
}
