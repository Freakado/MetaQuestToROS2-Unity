using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;
using System.Collections; // Required for Coroutines

public class VRServoController_rev1 : MonoBehaviour
{
    // --- ROS Settings ---
    private ROSConnection ros;
    public string panTopic = "/pan_duty_cycle";
    public string tiltTopic = "/tilt_duty_cycle";
    
    // --- FIX: Flag to track if we can publish yet ---
    private bool isRosReady = false;

    // --- VR Headset Reference ---
    public Transform centerEyeAnchor;

    // --- Servo Mapping ---
    [Header("Servo Mapping")]
    [Tooltip("The VR headset angle (in degrees) that corresponds to the minimum duty cycle.")]
    public float minAngle = -45.0f;
    [Tooltip("The VR headset angle (in degrees) that corresponds to the maximum duty cycle.")]
    public float maxAngle = 45.0f;
    [Tooltip("The minimum duty cycle for the servos (e.g., 2.5).")]
    public float minDutyCycle = 2.5f;
    [Tooltip("The maximum duty cycle for the servos (e.g., 12.5).")]
    public float maxDutyCycle = 12.5f;

    // --- Publishing Rate Limiter ---
    [Header("Performance")]
    [Tooltip("How many times per second to publish updates. 10 is a good starting point.")]
    public float publishFrequency = 10.0f;
    private float timeSinceLastPublish;

    void Start()
    {
        // --- ROS Connection Setup ---
        ros = ROSConnection.GetOrCreateInstance();
        
        // --- Safety Check ---
        if (centerEyeAnchor == null)
        {
            Debug.LogError("CenterEyeAnchor is not assigned! Please drag the CenterEyeAnchor from your OVRPlayerController into this script's slot in the Inspector.");
            this.enabled = false;
            return; // Stop execution if not assigned
        }
        
        // --- FIX: Use a Coroutine to register topics after a delay ---
        StartCoroutine(RegisterTopicsAfterConnection());
    }

    // --- FIX: New Coroutine to handle topic registration ---
    IEnumerator RegisterTopicsAfterConnection()
    {
        // Wait until the ROS connection is fully established.
        yield return new WaitUntil(() => ros.HasConnectionThread);
        
        // Wait a very small extra amount of time to be safe.
        yield return new WaitForSeconds(0.5f);
        
        // Now, register the publishers.
        ros.RegisterPublisher<Float32Msg>(panTopic);
        ros.RegisterPublisher<Float32Msg>(tiltTopic);
        
        // Set our flag to true so the Update loop can start publishing.
        isRosReady = true;
        Debug.Log("ROS publishers registered and ready.");
    }

    void Update()
    {
        // --- FIX: Check if ROS is ready before doing anything ---
        if (!isRosReady)
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

        // --- Map Angles to Duty Cycles ---
        float panDuty = MapValue(yaw, minAngle, maxAngle, minDutyCycle, maxDutyCycle);
        float tiltDuty = MapValue(pitch, minAngle, maxAngle, maxDutyCycle, minDutyCycle);

        // --- Create and Publish ROS Messages ---
        Float32Msg panMsg = new Float32Msg(panDuty);
        Float32Msg tiltMsg = new Float32Msg(tiltDuty);

        ros.Publish(panTopic, panMsg);
        ros.Publish(tiltTopic, tiltMsg);
    }
    
    private float MapValue(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        value = Mathf.Clamp(value, fromMin, fromMax);
        return (value - fromMin) * (toMax - toMin) / (fromMax - fromMin) + toMin;
    }
}