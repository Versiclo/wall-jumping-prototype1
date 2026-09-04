using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    [SerializeField] GameObject player;
    [SerializeField] Vector3 cameraOffset;
    void Awake()
    {
        if (cameraOffset == null)
        {
            cameraOffset = new Vector3(8.5f, 3.5f, -10);
        }
    }

    void LateUpdate()
    {
        transform.position = player.transform.position + cameraOffset;
    }
}
