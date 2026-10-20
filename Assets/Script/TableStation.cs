using UnityEngine;

// Put this on a table in the casino. It links the table to its minigame and camera.
[RequireComponent(typeof(Collider))]
public class TableStation : MonoBehaviour
{
    [SerializeField] private string displayName;
    [SerializeField] private GameObject minigamePanel;   // The minigame UI for this table
    [SerializeField] private GameObject tableCamera;     // The Cinemachine camera that zooms into this table

    private Collider tableCollider;

    public string DisplayName => displayName;

    private void Awake()
    {
        tableCollider = GetComponent<Collider>();
    }

    // Distance across the floor from a position to the nearest edge of this table
    public float DistanceTo(Vector3 position)
    {
        Vector3 closestPoint = tableCollider.ClosestPoint(position);
        closestPoint.y = position.y;
        return Vector3.Distance(position, closestPoint);
    }

    // Opening the table shows its minigame and zooms the camera in
    public void SetOpen(bool open)
    {
        minigamePanel.SetActive(open);
        tableCamera.SetActive(open);
    }
}