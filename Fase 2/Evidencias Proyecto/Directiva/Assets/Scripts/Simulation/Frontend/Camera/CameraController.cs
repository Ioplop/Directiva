using UnityEngine;

public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }
    [Header("References")]
    [SerializeField] private Transform focusPoint;
    [SerializeField] private Transform pitchPivot;
    [SerializeField] private Transform cameraPosition;
    [SerializeField] private new Camera camera;

    [Header("Zoom options")]
    [SerializeField] private float maxZoom = 0.2f;
    [SerializeField] private float minZoom = 1.5f;
    [SerializeField] private float zoomSpeed = 0.1f;

    [Header("Move options")]
    [SerializeField] private float moveSpeed = 1f;

    [Header("Rotate options")]
    [SerializeField] private float yawSpeed = 360f;
    [SerializeField] private float pitchSpeed = 30f;
    [SerializeField] private float maxPitch = 90f;
    [SerializeField] private float minPitch = 30f;

    // The camera focus point can only ever stay within these bounds.
    private Rect worldBounds;
    

    private float Distance
    {
        get { return -cameraPosition.position.z; }
        set {
            var dist = Mathf.Clamp(value, minZoom, maxZoom);
            cameraPosition.position = new Vector3(cameraPosition.position.x, cameraPosition.position.y, -dist); 
        }
    }

    private float Yaw
    {
        get { return focusPoint.localEulerAngles.y; }
        set { focusPoint.localEulerAngles = new Vector3(focusPoint.localEulerAngles.x, value % 360, focusPoint.localEulerAngles.z); }
    }

    private float Pitch
    {
        get { return pitchPivot.localEulerAngles.x; }
        set {
            var pitch = Mathf.Clamp(value, minPitch, maxPitch);
            pitchPivot.localEulerAngles = new Vector3(pitch, pitchPivot.localEulerAngles.y, pitchPivot.localEulerAngles.z); 
        }
    }

    private Vector2 Position
    {
        get { return (Vector2)focusPoint.position; }
        set {
            float x = Mathf.Clamp(value.x, worldBounds.xMin, worldBounds.xMax);
            float z = Mathf.Clamp(value.y, worldBounds.yMin, worldBounds.yMax);
            focusPoint.position = new Vector3(x, focusPoint.position.y, z);
        }
    }

    // Something needs to call this!
    public void SetBounds(Rect bounds)
    {
        worldBounds = bounds;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(focusPoint.gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        Rotate();
        Zoom();
        Move();
    }

    private void Move()
    {
        // How to get WASD or camera movement controls?
        float horizontal = 0f;
        float vertical = 0f;

        float maxSpeed = Distance * moveSpeed;
        var movement = Vector2.ClampMagnitude(new Vector2(horizontal, vertical) * maxSpeed, maxSpeed);
        Position += movement * Time.deltaTime;        
    }

    private void Rotate()
    {
        // How to get horizontal and vertical mouse movement while also holding middle mouse button?
        float vertical = 0f;
        float horizontal = 0f;

        Pitch += vertical * pitchSpeed * Time.deltaTime;
        Yaw += horizontal * yawSpeed * Time.deltaTime;
    }

    private void Zoom()
    {
        // How to get how much the scrollwheel is turning and its direction?
        float wheel = 0f;

        Distance += Distance * wheel * zoomSpeed * Time.deltaTime;
    }
}
