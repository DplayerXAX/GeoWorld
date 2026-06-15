using UnityEngine;

// Shipping-friendly container for an authored level map. The dev workflow saves
// maps as JSON under persistentDataPath (LevelMapIO); that file does NOT ship in a
// build. Bake it into one of these assets via  GeoWorld ▸ Level Map ▸ Bake JSON →
// Asset, then point LevelMapController.mapAsset at it — the map now travels inside
// the build with no runtime file IO or per-platform path issues.
[CreateAssetMenu(menuName = "GeoWorld/Level Map Asset", fileName = "LevelMap")]
public class LevelMapAsset : ScriptableObject
{
    public LevelMapData data = new LevelMapData();
}
