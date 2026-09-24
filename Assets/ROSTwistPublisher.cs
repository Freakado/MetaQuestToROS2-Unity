using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Geometry;

public class RosTwistPublisher : MonoBehaviour
{
    [Header("Menu Interlock")]
    public RobotCommandMenu commandMenu;
    public RobotImmersionManager immersionManager;

    [Header("ROS Topic Configuration")]
    public string topicName = "/JeffCreate3/cmd_vel";

    [Header("Control Tuning")]
    public float maxLinearSpeed = 0.5f;
    public float maxAngularSpeed = 1.0f;
    public float thumbstickDeadzone = 0.08f;

    [Header("Publishing Rate")]
    public float publishFrequency = 20.0f;
    private float timeSinceLastPublish;

    private ROSConnection ros;
    private TwistMsg twistMessage;

    void Awake()
    {
        if (commandMenu == null)
            commandMenu = FindAnyObjectByType<RobotCommandMenu>();

        if (immersionManager == null)
            immersionManager = FindAnyObjectByType<RobotImmersionManager>();
    }

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<TwistMsg>(topicName);

        twistMessage = new TwistMsg
        {
            linear = new Vector3Msg(),
            angular = new Vector3Msg()
        };
    }

    void Update()
    {
        // Gate: Must be in InTheLoop immersion AND have teleop explicitly enabled in menu
        bool isModeActive = (immersionManager != null && immersionManager.currentMode == RobotImmersionManager.ImmersionMode.InTheLoop);
        bool isTeleopAllowed = (commandMenu != null && commandMenu.isTeleopAsserted);

        if (!isModeActive || !isTeleopAllowed)
        {
            return;
        }

        timeSinceLastPublish += Time.deltaTime;
        if (timeSinceLastPublish < (1.0f / publishFrequency))
        {
            return;
        }
        timeSinceLastPublish = 0.0f;

        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, OVRInput.Controller.LTouch);
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick, OVRInput.Controller.RTouch);

        // Fallback for keyboard/editor testing
        if (leftStick == Vector2.zero && rightStick == Vector2.zero)
        {
            leftStick.y = Input.GetAxis("Vertical");
            rightStick.x = -Input.GetAxis("Horizontal");
        }

        float linearX = Mathf.Abs(leftStick.y) > thumbstickDeadzone ? leftStick.y * maxLinearSpeed : 0.0f;
        float angularZ = Mathf.Abs(rightStick.x) > thumbstickDeadzone ? rightStick.x * maxAngularSpeed : 0.0f;

        twistMessage.linear.x = linearX;
        twistMessage.angular.z = angularZ;

        ros.Publish(topicName, twistMessage);
    }

    public void SendStopCommand()
    {
        if (twistMessage == null || ros == null) return;

        twistMessage.linear.x = 0.0;
        twistMessage.linear.y = 0.0;
        twistMessage.linear.z = 0.0;
        twistMessage.angular.x = 0.0;
        twistMessage.angular.y = 0.0;
        twistMessage.angular.z = 0.0;

        ros.Publish(topicName, twistMessage);
    }
}