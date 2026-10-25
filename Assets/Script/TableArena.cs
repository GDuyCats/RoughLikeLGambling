using UnityEngine;

// A giant table top where the tiny chibi plays a minigame
public class TableArena : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;                        // Where the chibi lands
    [SerializeField] private Vector2 walkMin = new Vector2(-14f, 92f);    // Walkable area, X and Z
    [SerializeField] private Vector2 walkMax = new Vector2(14f, 108f);

    public Transform SpawnPoint => spawnPoint;
    public Vector2 WalkMin => walkMin;
    public Vector2 WalkMax => walkMax;

    // Draws the walkable area as a yellow box in the Scene view when this object is selected
    private void OnDrawGizmosSelected()
    {
        Vector3 center = new Vector3((walkMin.x + walkMax.x) / 2f, 0.1f, (walkMin.y + walkMax.y) / 2f);
        Vector3 size = new Vector3(walkMax.x - walkMin.x, 0.1f, walkMax.y - walkMin.y);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(center, size);
    }
}