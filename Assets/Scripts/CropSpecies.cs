using UnityEngine;

[CreateAssetMenu(fileName = "CropSpecies", menuName = "Topor/Crop Species")]
public class CropSpecies : ScriptableObject
{
    public string speciesId;
    public string displayName;
    public int baseBitesToEat = 1;
    public int seedValue = 1;
    public Sprite icon;
    public Color tint = Color.white;
}
