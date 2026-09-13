using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Nav;

public class MapVisualizer : MonoBehaviour
{
    [Header("ROS Connection")]
    [Tooltip("Target ROSConnection on Port 10000")]
    public ROSConnection controlConnection;
    public string mapTopic = "/JeffCreate3/map";

    [Header("Display Target")]
    public Renderer mapRenderer;

    private Texture2D mapTexture;
    private Color32[] colorBuffer;
    private bool newMapReceived = false;
    private readonly object mapLock = new object();
    private OccupancyGridMsg latestMapMsg;

    void Start()
    {
        if (controlConnection == null)
            controlConnection = ROSConnection.GetOrCreateInstance();

        controlConnection.Subscribe<OccupancyGridMsg>(mapTopic, OnMapReceived);
    }

    void Update()
    {
        if (!newMapReceived) return;

        OccupancyGridMsg msg;
        lock (mapLock)
        {
            msg = latestMapMsg;
            newMapReceived = false;
        }

        if (msg == null) return;

        int width = (int)msg.info.width;
        int height = (int)msg.info.height;

        if (width <= 0 || height <= 0) return;

        // Reallocate texture if map size changes
        if (mapTexture == null || mapTexture.width != width || mapTexture.height != height)
        {
            mapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            mapTexture.filterMode = FilterMode.Point;
            colorBuffer = new Color32[width * height];

            if (mapRenderer != null)
                mapRenderer.material.mainTexture = mapTexture;
        }

        // Map occupancy values to RGBA
        // -1: Unknown (dark grey)
        // 0: Free space (almost transparent or subtle dark blue)
        // 1-100: Obstacles/Walls (bright cyan/white)
        for (int i = 0; i < msg.data.Length; i++)
        {
            sbyte val = msg.data[i];
            if (val == 0)
            {
                colorBuffer[i] = new Color32(20, 24, 30, 200); // Free space floor
            }
            else if (val == 100)
            {
                colorBuffer[i] = new Color32(0, 255, 255, 255); // Wall / Obstacle
            }
            else if (val > 0)
            {
                byte intensity = (byte)Mathf.Clamp(val * 2.55f, 0, 255);
                colorBuffer[i] = new Color32(intensity, 80, 80, 255); // Inflation / cost
            }
            else
            {
                colorBuffer[i] = new Color32(10, 10, 12, 160); // Unknown void
            }
        }

        mapTexture.SetPixels32(colorBuffer);
        mapTexture.Apply();

        // Scale the Quad to preserve actual map aspect ratio
        if (mapRenderer != null)
        {
            float aspect = (float)width / Mathf.Max(1, height);
            Vector3 s = mapRenderer.transform.localScale;
            mapRenderer.transform.localScale = new Vector3(s.y * aspect, s.y, 1.0f);
        }
    }

    private void OnMapReceived(OccupancyGridMsg msg)
    {
        lock (mapLock)
        {
            latestMapMsg = msg;
            newMapReceived = true;
        }
    }
}