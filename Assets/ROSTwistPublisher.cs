using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
// We need these message types for Twist
using RosMessageTypes.Geometry;

public class RosTwistPublisher : MonoBehaviour
{
    // --- ROS Settings ---
    private ROSConnection ros;
    public string topicName = "/JeffCreate3/cmd_vel"; // The standard topic for velocity commands

    // --- Control Tuning ---
    [Header("Tuning")]
    [Tooltip("The maximum forward/backward speed in meters/second.")]
    public float maxLinearSpeed = 0.5f;
    [Tooltip("The maximum turning speed in radians/second.")]
    public float maxAngularSpeed = 1.0f;

    // --- Publishing Rate Limiter ---
    [Header("Performance")]
    [Tooltip("How many times per second to publish updates. 20 is a good value for teleop.")]
    public float publishFrequency = 20.0f;
    private float timeSinceLastPublish;

    // We will re-use this message object to be more efficient
    private TwistMsg twistMessage;

    void Start()
    {
        // --- ROS Connection Setup ---
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<TwistMsg>(topicName);

        // --- Initialize the Twist Message ---
        twistMessage = new TwistMsg
        {
            linear = new Vector3Msg(),
            angular = new Vector3Msg()
        };

        Debug.Log($"Ready to publish Twist commands to {topicName}");
    }

    void Update()
    {
        // --- Rate Limiting Logic ---
        timeSinceLastPublish += Time.deltaTime;
        if (timeSinceLastPublish < (1.0f / publishFrequency))
        {
            return; // It's not time to publish yet
        }
        timeSinceLastPublish = 0.0f; // Reset the timer

        // --- Read Joystick Inputs from Meta Quest Controllers ---
        // OVRInput is part of the Meta SDK. It's the easiest way to get direct controller input.
        // PrimaryThumbstick is the Left Controller. SecondaryThumbstick is the Right.
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.RTouch);

        // --- Map Joystick Values to Twist Message ---
        // Left stick's Y-axis controls forward/backward speed (linear.x)
        twistMessage.linear.x = leftStick.y * maxLinearSpeed;
        
        // Left stick's X-axis can control strafing speed (linear.y). Many simple robots ignore this.
        // twistMessage.linear.y = leftStick.x * maxLinearSpeed; 
        
        // Right stick's X-axis controls turning speed (angular.z)
        twistMessage.angular.z = rightStick.x * maxAngularSpeed;
        
        // --- Publish the Message ---
        ros.Publish(topicName, twistMessage);
    }
}