using UnityEngine;

/// <summary>Keeps world-space UI (enemy health bars) facing the camera.</summary>
public class Billboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;
    }
}
