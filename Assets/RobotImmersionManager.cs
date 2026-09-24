using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;

public class RobotImmersionManager : MonoBehaviour
{
    public enum ImmersionMode
    {
        NoRobot,    // Passthrough + status bar only
        SomeRobot,  // Passthrough + status bar + centered floating FPV screen
        InTheLoop   // Blackout room + enlarged FPV screen + active teleop & QR scanning
    }

    [Header("ROS Connection Configuration")]
    public ROSConnection RosConnection_video;
    public ROSConnection RosConnection_general;

    [Header("ROS Video & Diagnostics")]
    public string imageTopic = "/JeffCreate3/realsense2_camera/color/image_raw/compressed";
    public string tempTopic = "/JeffCreate3/nuc_cpu_temperature";

    [Header("Robot Command / QR Scanner")]
    [Tooltip("ROS topic that triggers artifact/QR scanning and puzzle retrieval")]
    public string commandTopic = "/JeffCreate3/puzzle_command";
    [Tooltip("Message payload sent when triggering a QR scan")]
    public string commandMessage = "read_qr";

    [Header("Environment & Display References")]
    public OVRPassthroughLayer passthroughLayer;
    public Camera centerEyeCamera;
    public Transform centerEyeAnchor;
    public Renderer screenRenderer;
    public Transform fpvScreenTransform;

    [Header("Screen Sizing & Placement")]
    public float someRobotDistance = 1.3f;
    public float someRobotHeight = 0.75f;
    public float inRobotDistance = 1.0f;
    public float inRobotHeight = 0.8f;

    [Header("Gesture & Controller Input")]
    public OVRHand leftOVRHand;
    public OVRHand.HandFinger toggleFinger = OVRHand.HandFinger.Index;

    [Header("Status Bar / rqt Panel")]
    public GameObject debugCanvas;
    public Text debugText;

    [Header("Active State")]
    public ImmersionMode currentMode = ImmersionMode.InTheLoop;

    private Texture2D cameraTexture;
    private byte[] lastFrameBytes;
    private bool hasNewFrame = false;
    private readonly object frameLock = new object();

    private float cpuTemp = 0.0f;
    private int frameCount = 0;
    private float fpsTimer = 0.0f;
    private float currentFps = 0.0f;
    private float currentAspect = 1.333f;
    private bool wasLeftPinchingLastFrame = false;
    private string lastCommandSent = "None";

    void Awake()
    {
        RenderSettings.skybox = null;

        if (centerEyeCamera == null)
            centerEyeCamera = Camera.main;

        if (centerEyeCamera != null && centerEyeAnchor == null)
            centerEyeAnchor = centerEyeCamera.transform;

        if (passthroughLayer == null)
            passthroughLayer = FindAnyObjectByType<OVRPassthroughLayer>();

        if (OVRManager.instance != null)
            OVRManager.instance.isInsightPassthroughEnabled = true;

        if (leftOVRHand == null)
        {
            OVRHand[] hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
            foreach (var hand in hands)
            {
                if (hand.gameObject.name.ToLower().Contains("left") ||
                    (hand.transform.parent != null && hand.transform.parent.name.ToLower().Contains("left")))
                {
                    leftOVRHand = hand;
                    break;
                }
            }
        }

        if (RosConnection_video == null || RosConnection_general == null)
        {
            ROSConnection[] connections = FindObjectsByType<ROSConnection>(FindObjectsSortMode.None);
            foreach (var conn in connections)
            {
                if (conn.RosPort == 10001 || conn.gameObject.name.ToLower().Contains("video"))
                    RosConnection_video = conn;
                else if (conn.RosPort == 10000 || conn.gameObject.name.ToLower().Contains("control") || conn.gameObject.name.ToLower().Contains("general"))
                    RosConnection_general = conn;
            }

            if (RosConnection_video == null) RosConnection_video = ROSConnection.GetOrCreateInstance();
            if (RosConnection_general == null) RosConnection_general = ROSConnection.GetOrCreateInstance();
        }
    }

    void Start()
    {
        cameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (screenRenderer != null)
            screenRenderer.material.mainTexture = cameraTexture;

        // Register Video & Diagnostic Subscriptions
        if (RosConnection_video != null)
            RosConnection_video.Subscribe<CompressedImageMsg>(imageTopic, OnImageReceived);

        if (RosConnection_general != null)
        {
            RosConnection_general.Subscribe<Float32Msg>(tempTopic, msg => { cpuTemp = msg.data; });
            RosConnection_general.RegisterPublisher<StringMsg>(commandTopic);
        }

        SetMode(currentMode);
    }

    void Update()
    {
        // 1. Mode Cycling (Left Pinch, Left Grip, or Tab key)
        bool cycleTrigger = DetectLeftHandPinchDown() ||
                            OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch) ||
                            (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame);

        if (cycleTrigger)
        {
            currentMode = (ImmersionMode)(((int)currentMode + 1) % 3);
            SetMode(currentMode);
            Debug.Log($"<color=yellow>[Immersion Mode]:</color> Switched to {currentMode}");
        }

        // 2. Artifact / QR Code Scan Trigger (X or Y button on Left Controller)
        bool triggerQr = OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch) || // X Button
                         OVRInput.GetDown(OVRInput.Button.Four, OVRInput.Controller.LTouch) ||  // Y Button
                         (Keyboard.current != null && (Keyboard.current.xKey.wasPressedThisFrame || Keyboard.current.yKey.wasPressedThisFrame));

        if (triggerQr)
        {
            SendRobotCommand(commandMessage);
        }

        // Decode incoming video frames
        if (hasNewFrame)
        {
            byte[] data;
            lock (frameLock)
            {
                data = lastFrameBytes;
                hasNewFrame = false;
            }

            if (data != null && cameraTexture.LoadImage(data))
            {
                currentAspect = (float)cameraTexture.width / Mathf.Max(1, cameraTexture.height);
                ApplyScreenDimensions();
            }
        }

        // Update HUD diagnostics
        frameCount++;
        fpsTimer += Time.deltaTime;
        if (fpsTimer >= 1.0f)
        {
            currentFps = frameCount / fpsTimer;
            frameCount = 0;
            fpsTimer = 0.0f;
            UpdateDebugHUD();
        }
    }

    public void SendRobotCommand(string message)
    {
        if (RosConnection_general != null)
        {
            StringMsg msg = new StringMsg(message);
            RosConnection_general.Publish(commandTopic, msg);
            lastCommandSent = message;
            Debug.Log($"<color=green>[Command Sent]:</color> Published '{message}' to {commandTopic}");
        }
    }

    private bool DetectLeftHandPinchDown()
    {
        bool isPinchingNow = false;
        if (leftOVRHand != null && leftOVRHand.IsTracked)
            isPinchingNow = leftOVRHand.GetFingerIsPinching(toggleFinger);
        else
            isPinchingNow = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.LHand);

        bool pinchDown = isPinchingNow && !wasLeftPinchingLastFrame;
        wasLeftPinchingLastFrame = isPinchingNow;
        return pinchDown;
    }

    public void SetMode(ImmersionMode mode)
    {
        currentMode = mode;

        if (debugCanvas != null)
            debugCanvas.SetActive(true);

        switch (mode)
        {
            case ImmersionMode.NoRobot:
                ConfigurePassthrough(true);
                if (screenRenderer != null)
                    screenRenderer.gameObject.SetActive(false);
                break;

            case ImmersionMode.SomeRobot:
                ConfigurePassthrough(true);
                if (screenRenderer != null)
                    screenRenderer.gameObject.SetActive(true);

                // PinScreenCentrally(someRobotDistance);       // Do not pin
                ApplyScreenDimensions();
                break;

            case ImmersionMode.InTheLoop:
                ConfigurePassthrough(false); // Blackout physical surroundings
                if (screenRenderer != null)
                    screenRenderer.gameObject.SetActive(true);

                // PinScreenCentrally(inRobotDistance);
                ApplyScreenDimensions();
                break;
        }
    }

    private void PinScreenCentrally(float distance)
    {
        if (fpvScreenTransform == null || centerEyeAnchor == null) return;

        fpvScreenTransform.SetParent(centerEyeAnchor, false);
        fpvScreenTransform.localPosition = new Vector3(0f, 0f, distance);
        fpvScreenTransform.localRotation = Quaternion.identity;
    }

    private void ApplyScreenDimensions()
    {
        if (fpvScreenTransform == null) return;

        float targetHeight = (currentMode == ImmersionMode.InTheLoop) ? inRobotHeight : someRobotHeight;
        float targetWidth = targetHeight * currentAspect;
        fpvScreenTransform.localScale = new Vector3(targetWidth, targetHeight, 1.0f);
    }

    private void ConfigurePassthrough(bool enablePassthrough)
    {
        if (enablePassthrough)
        {
            if (passthroughLayer != null)
            {
                passthroughLayer.hidden = false;
                passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
            }

            if (centerEyeCamera != null)
            {
                centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                centerEyeCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            }
        }
        else
        {
            if (passthroughLayer != null)
                passthroughLayer.hidden = true;

            if (centerEyeCamera != null)
            {
                centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                centerEyeCamera.backgroundColor = Color.black;
            }
        }
    }

    private void OnImageReceived(CompressedImageMsg msg)
    {
        lock (frameLock)
        {
            lastFrameBytes = msg.data;
            hasNewFrame = true;
        }
    }

    private void UpdateDebugHUD()
    {
        if (debugText != null)
        {
            string teleopStatus = (currentMode == ImmersionMode.InTheLoop) ? "<color=green>ONLINE</color>" : "<color=red>LOCKED</color>";
            debugText.text = $"<b>SYSTEM DIAGNOSTICS</b>\n" +
                             $"Teleoperation: {teleopStatus}\n" +
                             $"Stream FPS: {currentFps:F1}\n" +
                             $"NUC CPU Temp: {cpuTemp:F1}°C\n" +
                             $"Immersion Mode: <b>{currentMode}</b>\n" +
                             $"Scan QR: [X/Y Button] | Last: {lastCommandSent}";
        }
    }
}