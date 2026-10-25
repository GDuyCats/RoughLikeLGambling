using UnityEngine;

// Put this on a table in the casino. It links the table to its minigame, camera, boss spot and (optionally) a giant arena.
[RequireComponent(typeof(Collider))]
public class TableStation : MonoBehaviour
{
    [SerializeField] private string displayName;
    [SerializeField] private MinigameType minigame;
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private GameObject tableCamera;   // For a table with an arena, use the arena camera
    [SerializeField] private Transform bossSpot;
    [SerializeField] private TableArena arena;         // Leave empty for a table played from the casino floor

    private Collider tableCollider;

    public string DisplayName => displayName;
    public MinigameType Minigame => minigame;
    public Transform BossSpot => bossSpot;
    public TableArena Arena => arena;

    private void Awake()
    {
        tableCollider = GetComponent<Collider>();
    }

    public float DistanceTo(Vector3 position)
    {
        Vector3 closestPoint = tableCollider.ClosestPoint(position);
        closestPoint.y = position.y;
        return Vector3.Distance(position, closestPoint);
    }

    // The point on the table's edge nearest to a position (used for the climbing hop)
    public Vector3 ClosestPoint(Vector3 position)
    {
        return tableCollider.ClosestPoint(position);
    }

    public void SetOpen(bool open)
    {
        minigamePanel.SetActive(open);
        tableCamera.SetActive(open);
    }
}