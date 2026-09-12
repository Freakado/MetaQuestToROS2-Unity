using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;

public class RosCameraSubscriberSkybox : MonoBehaviour
{
    [Header("ROS Topic Configuration")]
    [Tooltip("Full namespaced topic for compressed camera frames.")]
    public string imageTopic = "/image_raw/compressed";

    private ROSConnection ros;
    private Texture2D cameraTexture;
    private byte[] lastFrameBytes;
    private bool hasNewFrame = false;
    private readonly object frameLock = new object();

    private Material skyboxMaterial;
    private string texturePropertyName = "_MainTex";

    void Start()
    {
        // 1. Allocate a single persistent texture buffer to eliminate per-frame allocations
        cameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);

        // 2. Validate skybox material assignment
        if (RenderSettings.skybox == null)
        {
            Debug.LogError("No Skybox Material is assigned in Scene Lighting settings!");
            this.enabled = false;
            return;
        }

        skyboxMaterial = RenderSettings.skybox;

        // 3. Resolve shader property: Panoramic shaders use _Tex, standard shaders use _MainTex
        if (skyboxMaterial.HasProperty("_Tex"))
        {
            texturePropertyName = "_Tex";
        }
        else if (skyboxMaterial.HasProperty("_MainTex"))
        {
            texturePropertyName = "_MainTex";
        }
        else
        {
            Debug.LogWarning($"Skybox shader does not expose '_Tex' or '_MainTex'. Property updates may fail.");
        }

        // 4. Initialize ROS Connection and subscribe
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<CompressedImageMsg>(imageTopic, ReceiveImageCallback);
        Debug.Log($"<color=cyan>Subscribed to:</color> {imageTopic}");
    }

    void Update()
    {
        if (!hasNewFrame) return;

        byte[] frameData;
        lock (frameLock)
        {
            frameData = lastFrameBytes;
            hasNewFrame = false;
        }

        if (frameData != null && frameData.Length > 0)
        {
            // LoadImage decodes JPEG/PNG data in-place onto the existing GPU texture
            if (cameraTexture.LoadImage(frameData))
            {
                skyboxMaterial.SetTexture(texturePropertyName, cameraTexture);
            }
        }
    }

    private void ReceiveImageCallback(CompressedImageMsg message)
    {
        // Safe buffer swap from the ROS connection thread
        lock (frameLock)
        {
            lastFrameBytes = message.data;
            hasNewFrame = true;
        }
    }

    void OnDestroy()
    {
        // Clean up native GPU texture memory on exit
        if (cameraTexture != null)
        {
            Destroy(cameraTexture);
        }
    }
}