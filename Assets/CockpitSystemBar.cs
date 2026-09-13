using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.RclInterfaces;
using RosMessageTypes.Std;
using RosMessageTypes.Geometry;

public class CockpitSystemBar : MonoBehaviour
{
    [Header("ROS Control Connection")]
    public ROSConnection controlConnection;

    [Header("ROS Topics")]
    public string rosoutTopic = "/rosout";
    public string cpuTempTopic = "/JeffCreate3/nuc_cpu_temperature";
    //public string exploreTopic = "/JeffCreate3/start_explore";
    public string cmdVelTopic = "/JeffCreate3/cmd_vel";

    [Header("UI Strip References")]
    public Text statusStripText;
    public Text logTickerText;
    public Image logTickerBackground;

    private float cpuTemp = 0.0f;
    private string lastLogMsg = "SYSTEM READY";
    private byte lastLogLevel = LogMsg.INFO;
    private float logExpiryTimer = 0.0f;
    private bool isExploring = false;

    private readonly object lockObj = new object();
    private LogMsg pendingLog;

    void Start()
    {
        if (controlConnection == null)
            controlConnection = ROSConnection.GetOrCreateInstance();

        // controlConnection.RegisterPublisher<StringMsg>(exploreTopic);
        controlConnection.RegisterPublisher<TwistMsg>(cmdVelTopic);

        controlConnection.Subscribe<LogMsg>(rosoutTopic, OnLogReceived);
        controlConnection.Subscribe<Float32Msg>(cpuTempTopic, msg => { cpuTemp = msg.data; });

        UpdateHUDDisplay();
    }

    void Update()
    {
        // 1. Check E-STOP Trigger: Spacebar or Left Controller 'Y' Button
        // bool estopTriggered = (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
        //                       OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch);

        // if (estopTriggered)
        //     TriggerEmergencyStop();

        // // 2. Toggle Autonomy: 'X' key
        // if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
        //     ToggleAutonomousExplore();

        // 3. Process new Log messages
        LogMsg log = null;
        lock (lockObj)
        {
            if (pendingLog != null)
            {
                log = pendingLog;
                pendingLog = null;
            }
        }

        if (log != null)
            DisplayLogEvent(log);

        // 4. Fade old log ticker messages after 8 seconds
        if (logExpiryTimer > 0)
        {
            logExpiryTimer -= Time.deltaTime;
            if (logExpiryTimer <= 0 && logTickerBackground != null)
                logTickerBackground.color = new Color(0, 0, 0, 0.4f);
        }

        UpdateHUDDisplay();
    }

    private void OnLogReceived(LogMsg msg)
    {
        // Discard DEBUG (10) and normal INFO (20) traffic to protect UI thread
        if (msg.level < LogMsg.WARN) return;

        lock (lockObj)
        {
            pendingLog = msg;
        }
    }

    private void DisplayLogEvent(LogMsg log)
    {
        lastLogMsg = $"[{log.name}]: {log.msg}";
        lastLogLevel = log.level;
        logExpiryTimer = 8.0f;

        if (logTickerText != null)
            logTickerText.text = lastLogMsg;

        if (logTickerBackground != null)
        {
            if (log.level == LogMsg.WARN)
                logTickerBackground.color = new Color(0.8f, 0.5f, 0.0f, 0.85f); // Amber
            else if (log.level >= LogMsg.ERROR)
                logTickerBackground.color = new Color(0.85f, 0.1f, 0.1f, 0.85f); // Red
        }
    }

    private void UpdateHUDDisplay()
    {
        if (statusStripText != null)
        {
            string modeStr = isExploring ? "<color=green>AUTONOMOUS EXPLORE</color>" : "<color=cyan>TELEOP MANUAL</color>";
            statusStripText.text = $"<b>SYSTEM STATUS</b> | NUC: {cpuTemp:F1}°C | MODE: {modeStr} | [Y/SPACE]: E-STOP";
        }
    }

    // public void TriggerEmergencyStop()
    // {
    //     Debug.LogWarning("<color=red>[EMERGENCY STOP TRIGGERED]</color>");
    //     isExploring = false;

    //     // Publish immediate stop Twist
    //     TwistMsg stopTwist = new TwistMsg();
    //     controlConnection.Publish(cmdVelTopic, stopTwist);

    //     // Send cancel to autonomy explore
    //     // controlConnection.Publish(exploreTopic, new StringMsg("cancel"));

    //     DisplayLogEvent(new LogMsg
    //     {
    //         level = LogMsg.ERROR,
    //         name = "E-STOP",
    //         msg = "HARDWARE STOP ENGAGED. Velocity zeroed."
    //     });
    // }

    // public void ToggleAutonomousExplore()
    // {
    //     isExploring = !isExploring;
    //     string cmd = isExploring ? "start" : "stop";
    //     controlConnection.Publish(exploreTopic, new StringMsg(cmd));
    //     Debug.Log($"[Explore] Dispatched command: {cmd}");
    // }
}