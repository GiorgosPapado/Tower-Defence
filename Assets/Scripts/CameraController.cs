using UnityEngine;

/// <summary>WASD / arrow keys pan, mouse wheel zooms. Uses unscaled time so it works while paused.</summary>
public class CameraController : MonoBehaviour
{
    public float panSpeed = 25f;
    public float scrollSpeed = 400f;
    public float minY = 14f;
    public float maxY = 45f;
    public Vector2 xLimits = new Vector2(0f, 36f);
    public Vector2 zLimits = new Vector2(-12f, 22f);

    private void Update()
    {
        Vector3 p = transform.position;
        float dt = Time.unscaledDeltaTime;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) p.z += panSpeed * dt;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) p.z -= panSpeed * dt;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) p.x += panSpeed * dt;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) p.x -= panSpeed * dt;

        p.y -= Input.GetAxis("Mouse ScrollWheel") * scrollSpeed * dt;

        p.x = Mathf.Clamp(p.x, xLimits.x, xLimits.y);
        p.y = Mathf.Clamp(p.y, minY, maxY);
        p.z = Mathf.Clamp(p.z, zLimits.x, zLimits.y);
        transform.position = p;
    }
}
