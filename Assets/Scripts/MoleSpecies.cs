using UnityEngine;

[CreateAssetMenu(fileName = "MoleSpecies", menuName = "Topor/Mole Species")]
public class MoleSpecies : ScriptableObject
{
    public string speciesId;
    public string displayName;
    public int baseHitsToKill = 1;
    public bool useTelegraph = true;
    public float baseTelegraphDurationMs = 800f;
    public int bitesOnEscape = 1;
    public bool ignoresDecoy = false;
    public float baseSpeedMultiplier = 1f;
    public Color tint = Color.white;
}
