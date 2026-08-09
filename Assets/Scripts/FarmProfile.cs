using System;
using UnityEngine;

[CreateAssetMenu(fileName = "FarmProfile", menuName = "Topor/Farm Profile")]
public class FarmProfile : ScriptableObject
{
    public string farmId;
    public string displayName;
    public string theme;
    public Color backgroundColor = Color.green;
    public LevelProfile[] levels = Array.Empty<LevelProfile>();

    public LevelProfile GetLevel(int index)
    {
        if (index < 0 || index >= levels.Length)
            return null;
        return levels[index];
    }

    public int LevelCount => levels != null ? levels.Length : 0;
}
