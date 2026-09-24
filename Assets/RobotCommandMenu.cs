using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

public class RobotCommandMenu : MonoBehaviour
{
    [Serializable]
    public struct CustomCommand
    {
        public string buttonLabel;
        public string commandPayload;
    }

    [Header("ROS Topic Configuration")]
    public string systemCommandTopic = "/JeffCreate3/system_command";
    public ROSConnection rosConnection;

    [Header("Teleoperation Commands")]
    [Tooltip("Payload string sent to assert teleop control")]
    public string teleopStartCommand = "start_teleop";
    [Tooltip("Payload string sent when teleop is deactivated or mode is exited")]
    public string teleopStopCommand = "stop_teleop";

    [Header("External References")]
    public RobotImmersionManager immersionManager;
    public RosTwistPublisher twistPublisher;

    [Header("UI Hierarchy")]
    public GameObject menuCanvas;
    public Transform buttonContainer;      // Parent transform with a VerticalLayoutGroup
    public GameObject buttonPrefab;         // Template button with Button and Text components
    public Button teleopToggleButton;
    public Text teleopButtonText;

    [Header("Registered Custom Commands")]
    public List<CustomCommand> commandPalette = new List<CustomCommand>
    {
        new CustomCommand { buttonLabel = "Scan QR Code", commandPayload = "read_qr" },
        new CustomCommand { buttonLabel = "Solve Sudoku", commandPayload = "solve_sudoku" },
        new CustomCommand { buttonLabel = "Cancel Navigation", commandPayload = "cancel_nav" },
        new CustomCommand { buttonLabel = "Clear Costmaps", commandPayload = "clear_costmap" }
    };

    [Header("Status")]
    public bool isTeleopAsserted = false;

    private RobotImmersionManager.ImmersionMode previousMode;

    void Awake()
    {
        if (immersionManager == null)
            immersionManager = FindAnyObjectByType<RobotImmersionManager>();

        if (twistPublisher == null)
            twistPublisher = FindAnyObjectByType<RosTwistPublisher>();

        if (rosConnection == null)
        {
            ROSConnection[] connections = FindObjectsByType<ROSConnection>(FindObjectsSortMode.None);
            foreach (var conn in connections)
            {
                if (conn.RosPort == 10000 || conn.gameObject.name.ToLower().Contains("general") || conn.gameObject.name.ToLower().Contains("control"))
                {
                    rosConnection = conn;
                    break;
                }
            }
            if (rosConnection == null) rosConnection = ROSConnection.GetOrCreateInstance();
        }
    }

    void Start()
    {
        rosConnection.RegisterPublisher<StringMsg>(systemCommandTopic);
        BuildDynamicButtons();
        UpdateTeleopUI();

        if (immersionManager != null)
            previousMode = immersionManager.currentMode;
    }

    void Update()
    {
        // Monitor transitions out of InTheLoop mode
        if (immersionManager != null && immersionManager.currentMode != previousMode)
        {
            HandleImmersionStateChange(immersionManager.currentMode);
            previousMode = immersionManager.currentMode;
        }
    }

    private void HandleImmersionStateChange(RobotImmersionManager.ImmersionMode currentMode)
    {
        // Automatic Safety Interlock: if leaving InTheLoop while driving, force stop
        if (currentMode != RobotImmersionManager.ImmersionMode.InTheLoop && isTeleopAsserted)
        {
            StopTeleoperation();
        }

        if (menuCanvas != null)
        {
            menuCanvas.SetActive(currentMode == RobotImmersionManager.ImmersionMode.InTheLoop);
        }
    }

    public void ToggleTeleoperation()
    {
        if (isTeleopAsserted)
            StopTeleoperation();
        else
            StartTeleoperation();
    }

    public void StartTeleoperation()
    {
        isTeleopAsserted = true;
        PublishSystemCommand(teleopStartCommand);
        UpdateTeleopUI();
        Debug.Log("<color=green>[Command]:</color> Teleoperation asserted.");
    }

    public void StopTeleoperation()
    {
        isTeleopAsserted = false;
        PublishSystemCommand(teleopStopCommand);

        if (twistPublisher != null)
            twistPublisher.SendStopCommand();

        UpdateTeleopUI();
        Debug.Log("<color=yellow>[Command]:</color> Teleoperation stopped.");
    }

    public void PublishSystemCommand(string commandText)
    {
        if (string.IsNullOrEmpty(commandText) || rosConnection == null) return;

        StringMsg message = new StringMsg(commandText);
        rosConnection.Publish(systemCommandTopic, message);
        Debug.Log($"<color=cyan>[Command Outbound]:</color> '{commandText}' -> {systemCommandTopic}");
    }

    private void UpdateTeleopUI()
    {
        if (teleopButtonText != null)
        {
            teleopButtonText.text = isTeleopAsserted ? "Stop Teleoperation" : "Start Teleoperation";
        }

        if (teleopToggleButton != null)
        {
            ColorBlock cb = teleopToggleButton.colors;
            cb.normalColor = isTeleopAsserted ? new Color(0.85f, 0.25f, 0.25f) : new Color(0.15f, 0.75f, 0.35f);
            teleopToggleButton.colors = cb;
        }
    }

    private void BuildDynamicButtons()
    {
        if (teleopToggleButton != null)
        {
            teleopToggleButton.onClick.RemoveAllListeners();
            teleopToggleButton.onClick.AddListener(ToggleTeleoperation);
        }

        if (buttonContainer == null || buttonPrefab == null) return;

        foreach (var cmd in commandPalette)
        {
            GameObject btnInstance = Instantiate(buttonPrefab, buttonContainer);
            btnInstance.SetActive(true);

            Text label = btnInstance.GetComponentInChildren<Text>();
            if (label != null) label.text = cmd.buttonLabel;

            Button btnComponent = btnInstance.GetComponent<Button>();
            string capturedPayload = cmd.commandPayload;
            if (btnComponent != null)
            {
                btnComponent.onClick.AddListener(() => PublishSystemCommand(capturedPayload));
            }
        }
    }
}