using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class VRServoController : MonoBehaviour
{
    // --- ROS Settings ---
    private ROSConnection ros;
    // --- NEW: Updated topic names ---
    public string panTopic = "/pan_angle";
    public string tiltTopic = "/tilt_angle";

    // --- VR Headset Reference ---
    public Transform centerEyeAnchor;

    // --- Performance ---
    [Header("Performance")]
    [Tooltip("How many times per second to publish updates.")]
    public float publishFrequency = 10.0f;
    private float timeSinceLastPublish;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        
        // Register the publishers. We wait for the connection to be ready before using them.
        ros.RegisterPublisher<Float32Msg>(panTopic);
        ros.RegisterPublisher<Float32Msg>(tiltTopic);

        // Safety Check
        if (centerEyeAnchor == null)
        {
            Debug.LogError("CenterEyeAnchor is not assigned! Please link it in the Inspector.");
            this.enabled = false;
        }
    }

    void Update()
    {
        // Wait for the central RosConnectionManager to signal it's ready.
        if (!RosConnectionManager.IsReady)
        {
            return;
        }

        // --- Rate Limiting Logic ---
        timeSinceLastPublish += Time.deltaTime;
        if (timeSinceLastPublish < (1.0f / publishFrequency))
        {
            return;
        }
        timeSinceLastPublish = 0.0f;

        // --- Get Headset Rotation ---
        Vector3 eulerAngles = centerEyeAnchor.localEulerAngles;
        float yaw = (eulerAngles.y > 180) ? eulerAngles.y - 360f : eulerAngles.y;
        float pitch = (eulerAngles.x > 180) ? eulerAngles.x - 360f : eulerAngles.x;

        // --- Create and Publish ROS Messages with the raw angles ---
        Float32Msg panMsg = new Float32Msg(yaw);
        Float32Msg tiltMsg = new Float32Msg(pitch);

        ros.Publish(panTopic, panMsg);
        ros.Publish(tiltTopic, tiltMsg);

        Debug.Log($"Publishing Yaw: {yaw:F2}°, Pitch: {pitch:F2}°");
    }
}