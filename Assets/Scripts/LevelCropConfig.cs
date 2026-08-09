using System;

[Serializable]
public class LevelCropConfig
{
    public CropSpecies species;
    public int bonusBites = 0;
    public int weight = 1;

    public int TotalBites => species != null ? species.baseBitesToEat + bonusBites : 1 + bonusBites;
    public int SeedValue => species != null ? species.seedValue : 1;
}
