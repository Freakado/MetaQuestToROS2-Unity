using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;
using RosMessageTypes.Visualization;
using RosMessageTypes.Geometry;

public class RVizCockpitPanel : MonoBehaviour
{
    [Header("ROS Control Connection")]
    public ROSConnection controlConnection;

    [Header("ROS Topics")]
    public string mapTopic = "/JeffCreate3/map";
    public string odomTopic = "/JeffCreate3/odom";
    public string markerArrayTopic = "/JeffCreate3/discovered_artifact_array";

    [Header("Display Target")]
    public Renderer mapRenderer;
    public Text hudStatusText;

    private Texture2D mapTexture;
    private Color32[] colorBuffer;
    private float mapResolution = 0.05f;
    private Vector3 mapOrigin = Vector3.zero;
    private int mapWidth = 0;
    private int mapHeight = 0;
    private bool hasMap = false;

    // Visual indicators
    private Transform robotArrow;
    private readonly Dictionary<int, GameObject> artifactDiamonds = new Dictionary<int, GameObject>();

    // Thread buffering
    private OccupancyGridMsg pendingMap;
    private OdometryMsg pendingOdom;
    private MarkerArrayMsg pendingMarkers;
    private readonly object lockObj = new object();

    void Start()
    {
        if (controlConnection == null)
            controlConnection = ROSConnection.GetOrCreateInstance();

        controlConnection.Subscribe<OccupancyGridMsg>(mapTopic, msg => { lock(lockObj) pendingMap = msg; });
        controlConnection.Subscribe<OdometryMsg>(odomTopic, msg => { lock(lockObj) pendingOdom = msg; });
        controlConnection.Subscribe<MarkerArrayMsg>(markerArrayTopic, msg => { lock(lockObj) pendingMarkers = msg; });

        CreateRobotArrowIndicator();
    }

    void Update()
    {
        OccupancyGridMsg mapMsg = null;
        OdometryMsg odomMsg = null;
        MarkerArrayMsg markerMsg = null;

        lock (lockObj)
        {
            if (pendingMap != null) { mapMsg = pendingMap; pendingMap = null; }
            if (pendingOdom != null) { odomMsg = pendingOdom; pendingOdom = null; }
            if (pendingMarkers != null) { markerMsg = pendingMarkers; pendingMarkers = null; }
        }

        if (mapMsg != null) RenderRVizMap(mapMsg);
        if (odomMsg != null) UpdateRobotOdometry(odomMsg);
        if (markerMsg != null) UpdateArtifactDiamonds(markerMsg);
    }

    // --- 1. Map Renderer (Standard RViz Theme) ---
    private void RenderRVizMap(OccupancyGridMsg msg)
    {
        mapWidth = (int)msg.info.width;
        mapHeight = (int)msg.info.height;
        mapResolution = msg.info.resolution;
        mapOrigin = new Vector3((float)msg.info.origin.position.x, (float)msg.info.origin.position.y, 0);

        if (mapWidth <= 0 || mapHeight <= 0) return;

        if (mapTexture == null || mapTexture.width != mapWidth || mapTexture.height != mapHeight)
        {
            mapTexture = new Texture2D(mapWidth, mapHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };
            colorBuffer = new Color32[mapWidth * mapHeight];
            if (mapRenderer != null)
                mapRenderer.material.mainTexture = mapTexture;
        }

        for (int i = 0; i < msg.data.Length; i++)
        {
            sbyte val = msg.data[i];
            if (val == 0)
            {
                // Free space: Pure White
                colorBuffer[i] = new Color32(255, 255, 255, 255);
            }
            else if (val == 100)
            {
                // Obstacles / Walls: Black
                colorBuffer[i] = new Color32(0, 0, 0, 255);
            }
            else if (val > 0)
            {
                // Inflation / Cost: Dark Grey gradient
                byte shade = (byte)(255 - (val * 2));
                colorBuffer[i] = new Color32(shade, shade, shade, 255);
            }
            else
            {
                // Unknown void: Classic RViz Medium Grey
                colorBuffer[i] = new Color32(128, 128, 128, 255);
            }
        }

        mapTexture.SetPixels32(colorBuffer);
        mapTexture.Apply();
        hasMap = true;
    }

    // --- 2. Odometry Indicator (Red Heading Arrow) ---
    private void UpdateRobotOdometry(OdometryMsg msg)
    {
        if (!hasMap || robotArrow == null) return;

        float x = (float)msg.pose.pose.position.x;
        float y = (float)msg.pose.pose.position.y;

        robotArrow.localPosition = WorldToQuadLocal(x, y);

        // Yaw angle from quaternion
        QuaternionMsg q = msg.pose.pose.orientation;
        float yaw = Mathf.Atan2(2.0f * (float)(q.w * q.z + q.x * q.y), 1.0f - 2.0f * (float)(q.y * q.y + q.z * q.z)) * Mathf.Rad2Deg;

        // Orient arrow along heading on Quad face
        robotArrow.localRotation = Quaternion.Euler(0, 0, yaw - 90f);

        if (hudStatusText != null)
        {
            hudStatusText.text = $"<b>RVIZ HUD</b>\nPose: [{x:F2}, {y:F2}]\nYaw: {yaw:F1}°\nArtifacts: {artifactDiamonds.Count}";
        }
    }

    // --- 3. Artifact Indicators (Yellow Diamonds + Tag ID) ---
    private void UpdateArtifactDiamonds(MarkerArrayMsg msg)
    {
        if (!hasMap) return;

        foreach (MarkerMsg m in msg.markers)
        {
            if (m.action == 0) // ADD / MODIFY
            {
                if (!artifactDiamonds.TryGetValue(m.id, out GameObject diamondObj))
                {
                    diamondObj = CreateYellowDiamond(m.id);
                    artifactDiamonds[m.id] = diamondObj;
                }

                diamondObj.transform.localPosition = WorldToQuadLocal((float)m.pose.position.x, (float)m.pose.position.y);
            }
            else if (m.action == 2 && artifactDiamonds.TryGetValue(m.id, out GameObject toRemove)) // DELETE
            {
                Destroy(toRemove);
                artifactDiamonds.Remove(m.id);
            }
            else if (m.action == 3) // DELETE ALL
            {
                foreach (var kvp in artifactDiamonds) Destroy(kvp.Value);
                artifactDiamonds.Clear();
            }
        }
    }

    private Vector3 WorldToQuadLocal(float xRos, float yRos)
    {
        float mapSizeX = mapWidth * mapResolution;
        float mapSizeY = mapHeight * mapResolution;
        if (mapSizeX <= 0 || mapSizeY <= 0) return Vector3.zero;

        float u = (xRos - mapOrigin.x) / mapSizeX;
        float v = (yRos - mapOrigin.y) / mapSizeY;
        return new Vector3(u - 0.5f, v - 0.5f, -0.02f);
    }

    // --- Procedural 3D Visual Generators ---
    private void CreateRobotArrowIndicator()
    {
        GameObject root = new GameObject("RobotHeadingArrow");
        root.transform.SetParent(transform, false);

        // Body: Red cone or cylinder
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        body.transform.SetParent(root.transform, false);
        body.transform.localScale = new Vector3(0.04f, 0.002f, 0.06f);
        body.transform.localPosition = Vector3.zero;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.red };
        body.GetComponent<Renderer>().material = mat;
        Destroy(body.GetComponent<Collider>());

        // Tip pointer
        GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        tip.transform.SetParent(root.transform, false);
        tip.transform.localScale = new Vector3(0.03f, 0.003f, 0.03f);
        tip.transform.localPosition = new Vector3(0, 0, 0.035f);
        tip.GetComponent<Renderer>().material = mat;
        Destroy(tip.GetComponent<Collider>());

        robotArrow = root.transform;
    }

    private GameObject CreateYellowDiamond(int tagId)
    {
        GameObject diamond = new GameObject($"ArtifactDiamond_{tagId}");
        diamond.transform.SetParent(transform, false);

        // Cube rotated 45 deg around Z forms a diamond
        GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mesh.transform.SetParent(diamond.transform, false);
        mesh.transform.localScale = new Vector3(0.04f, 0.04f, 0.004f);
        mesh.transform.localRotation = Quaternion.Euler(0, 0, 45f);

        var yellowMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.yellow };
        mesh.GetComponent<Renderer>().material = yellowMat;
        Destroy(mesh.GetComponent<Collider>());

        // Tag ID label above diamond
        GameObject labelObj = new GameObject("TagText");
        labelObj.transform.SetParent(diamond.transform, false);
        labelObj.transform.localPosition = new Vector3(0, 0.045f, -0.005f);
        labelObj.transform.localScale = new Vector3(0.015f, 0.015f, 0.015f);

        TextMesh tm = labelObj.AddComponent<TextMesh>();
        tm.text = $"#{tagId}";
        tm.fontSize = 28;
        tm.color = Color.yellow;
        tm.alignment = TextAlignment.Center;
        tm.anchor = TextAnchor.MiddleCenter;

        return diamond;
    }
}