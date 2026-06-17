using UnityEngine;

public class TitleCamera : MonoBehaviour
{
    public static TitleCamera Instance;

    [Header("Camera")]
    public Camera targetCamera;

    [Header("Zoom")]
    public float zoomTime = 0.35f;
    public float zoomDistance = 2.5f;

    [Header("FOV")]
    public float titleFOV = 60f;
    public float menuFOV = 44f;

    Vector3 startPos;
    Vector3 targetPos;

    float startFOV;
    float targetFOV;

    float timer;
    bool animating;

    void Awake()
    {
        Instance = this;

        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    void Start()
    {
        startPos = targetCamera.transform.position;
        startFOV = titleFOV;

        targetCamera.fieldOfView = titleFOV;
    }

    void Update()
    {
        if (!animating)
            return;

        timer += Time.deltaTime;

        float t = Mathf.Clamp01(timer / zoomTime);
        t = t * t * (3f - 2f * t);

        targetCamera.transform.position =
            Vector3.Lerp(startPos, targetPos, t);

        targetCamera.fieldOfView =
            Mathf.Lerp(startFOV, targetFOV, t);

        if (t >= 1f)
            animating = false;
    }

    public void ZoomToCube(Transform cube)
    {
        startPos = targetCamera.transform.position;
        startFOV = targetCamera.fieldOfView;

        targetPos =
            startPos +
            targetCamera.transform.forward * zoomDistance;

        targetFOV = menuFOV;

        timer = 0;
        animating = true;
    }

    public void ResetCamera()
    {
        startPos = targetCamera.transform.position;
        startFOV = targetCamera.fieldOfView;

        targetPos = Vector3.zero;
        targetFOV = titleFOV;

        timer = 0;
        animating = true;
    }
}