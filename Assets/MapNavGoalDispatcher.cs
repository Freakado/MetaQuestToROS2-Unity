using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using RosMessageTypes.Std;

[RequireComponent(typeof(LineRenderer))]
public class MapNavGoalDispatcher : MonoBehaviour
{
    [Header("ROS Control Connection")]
    public ROSConnection controlConnection;

    [Header("Topics")]
    public string goalTopic = "/goal_pose";
    public string pathTopic = "/JeffCreate3/transformed_global_plan";
    public string mapTopic = "/JeffCreate3/map";

    private LineRenderer pathRenderer;
    private float mapResolution = 0.05f;
    private int mapWidth = 0;
    private int mapHeight = 0;
    private Vector3 mapOrigin = Vector3.zero;
    private bool hasMap = false;

    private PathMsg pendingPath;
    private readonly object lockObj = new object();

    void Start()
    {
        if (controlConnection == null)
            controlConnection = ROSConnection.GetOrCreateInstance();

        controlConnection.RegisterPublisher<PoseStampedMsg>(goalTopic);
        controlConnection.Subscribe<PathMsg>(pathTopic, msg => { lock (lockObj) pendingPath = msg; });
        controlConnection.Subscribe<OccupancyGridMsg>(mapTopic, OnMapUpdated);

        // Setup LineRenderer for the Nav2 path
        pathRenderer = GetComponent<LineRenderer>();
        pathRenderer.useWorldSpace = false;
        pathRenderer.startWidth = 0.012f;
        pathRenderer.endWidth = 0.012f;
        pathRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
        {
            color = new Color(0.1f, 1.0f, 0.2f, 0.9f) // Neon green path
        };
        pathRenderer.positionCount = 0;
    }

    void Update()
    {
        // 1. Raycast Click-to-Goal on Map Quad (Desktop Mouse or VR Pointer)
        bool clickTriggered = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                              OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);

        if (clickTriggered)
            CheckRaycastGoal();

        // 2. Draw Nav2 global plan trajectory
        PathMsg path = null;
        lock (lockObj)
        {
            if (pendingPath != null)
            {
                path = pendingPath;
                pendingPath = null;
            }
        }

        if (path != null && hasMap)
            RenderNavPath(path);
    }

    private void CheckRaycastGoal()
    {
        Ray ray;
        if (Camera.main != null && Mouse.current != null)
            ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        else
            return;

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            if (hit.transform == this.transform)
            {
                Vector2 uv = hit.textureCoord;
                DispatchGoalFromUV(uv.x, uv.y);
            }
        }
    }

    private void DispatchGoalFromUV(float u, float v)
    {
        if (!hasMap || mapWidth == 0 || mapHeight == 0) return;

        // Convert UV [0, 1] back into metric ROS Map coordinates
        float worldX = (u * mapWidth * mapResolution) + mapOrigin.x;
        float worldY = (v * mapHeight * mapResolution) + mapOrigin.y;

        PoseStampedMsg goal = new PoseStampedMsg
        {
            header = new HeaderMsg
            {
                frame_id = "map",
                stamp = new RosMessageTypes.BuiltinInterfaces.TimeMsg()
            },
            pose = new PoseMsg
            {
                position = new PointMsg(worldX, worldY, 0.0),
                orientation = new QuaternionMsg(0, 0, 0, 1.0)
            }
        };

        controlConnection.Publish(goalTopic, goal);
        Debug.Log($"<color=cyan>[Nav2 Goal Dispatched]</color> Dispatched ({worldX:F2}, {worldY:F2}) to {goalTopic}");
    }

    private void RenderNavPath(PathMsg path)
    {
        if (path.poses == null || path.poses.Length == 0)
        {
            pathRenderer.positionCount = 0;
            return;
        }

        pathRenderer.positionCount = path.poses.Length;
        for (int i = 0; i < path.poses.Length; i++)
        {
            float x = (float)path.poses[i].pose.position.x;
            float y = (float)path.poses[i].pose.position.y;

            float u = (x - mapOrigin.x) / (mapWidth * mapResolution);
            float v = (y - mapOrigin.y) / (mapHeight * mapResolution);

            // Map UV to local Quad bounds [-0.5, 0.5]
            Vector3 localPos = new Vector3(Mathf.Clamp(u - 0.5f, -0.49f, 0.49f),
                                           Mathf.Clamp(v - 0.5f, -0.49f, 0.49f),
                                           -0.025f);
            pathRenderer.SetPosition(i, localPos);
        }
    }

    private void OnMapUpdated(OccupancyGridMsg msg)
    {
        mapWidth = (int)msg.info.width;
        mapHeight = (int)msg.info.height;
        mapResolution = msg.info.resolution;
        mapOrigin = new Vector3((float)msg.info.origin.position.x, (float)msg.info.origin.position.y, 0);
        hasMap = true;
    }
}