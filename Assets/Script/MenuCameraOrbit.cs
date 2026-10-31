using UnityEngine;

// Slowly turns this object around, so a camera attached to it circles the casino behind the main menu.
// Uses unscaled time, so it keeps moving even if the game is paused.
public class MenuCameraOrbit : MonoBehaviour
{
    [SerializeField] private float degreesPerSecond = 6f;

    private void Update()
    {
        transform.Rotate(Vector3.up, degreesPerSecond * Time.unscaledDeltaTime, Space.World);
    }
}