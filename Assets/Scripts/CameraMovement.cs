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

    void Start()
    {
        SetAspectRatio();
    }

    void LateUpdate()
    {
        transform.position = player.transform.position + cameraOffset;
    }

    void SetAspectRatio()
    {
        float targetAspect = 16f / 9f;
        float windowAspect = (float)Screen.width / Screen.height;
        float scaleHeight = windowAspect / targetAspect;

        Rect rect = new Rect(0, 0, 1, 1);
        if (scaleHeight < 1f)
        {
            rect.width = 1f;
            rect.height = scaleHeight;
            rect.x = 0;
            rect.y = (1f - scaleHeight) / 2f;
        }
        else
        {
            float scaleWidth = 1f / scaleHeight;
            rect.width = scaleWidth;
            rect.height = 1f;
            rect.x = (1f - scaleWidth) / 2f;
            rect.y = 0;
        }
        Camera.main.rect = rect;
    }
}
