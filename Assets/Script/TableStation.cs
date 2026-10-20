using UnityEngine;

// Put this on a table in the casino. It links the table to its minigame, camera and boss spot.
[RequireComponent(typeof(Collider))]
public class TableStation : MonoBehaviour
{
    [SerializeField] private string displayName;
    [SerializeField] private MinigameType minigame;
    [SerializeField] private GameObject minigamePanel;
    [SerializeField] private GameObject tableCamera;
    [SerializeField] private Transform bossSpot;   // Where the final boss stands at this table

    private Collider tableCollider;

    public string DisplayName => displayName;
    public MinigameType Minigame => minigame;
    public Transform BossSpot => bossSpot;

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

    public void SetOpen(bool open)
    {
        minigamePanel.SetActive(open);
        tableCamera.SetActive(open);
    }
}