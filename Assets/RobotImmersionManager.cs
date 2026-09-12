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
        NoRobot,    // Pure passthrough, screen hidden
        SomeRobot,  // Passthrough + floating FPV quad
        InRobot     // Blackout background + zoomed/immersive FPV quad
    }

    [Header("ROS Connection Configuration")]]
    public ROSConnection RosConnection_video;
    public ROSConnection RosConnection_general;

    [Header("ROS Topic Configuration")]
    public string imageTopic = "/JeffCreate3/realsense2_camera/color/image_raw/compressed";
    public string tempTopic = "/JeffCreate3/nuc_cpu_temperature";

    [Header("Environment & Display References")]
    public OVRPassthroughLayer passthroughLayer;
    public Camera centerEyeCamera;
    public Renderer screenRenderer;
    public Transform fpvScreenTransform;
    public Transform centerEyeAnchor;

    [Header("Debug / rqt Panel")]
    public GameObject debugCanvas;
    public Text debugText;

    [Header("Active State")]
    public ImmersionMode currentMode = ImmersionMode.SomeRobot;

    private Texture2D cameraTexture;
    private byte[] lastFrameBytes;
    private bool hasNewFrame = false;
    private readonly object frameLock = new object();

    private float cpuTemp = 0.0f;
    private int frameCount = 0;
    private float fpsTimer = 0.0f;
    private float currentFps = 0.0f;
    private Transform originalScreenParent;

    void Start()
    {
        if (fpvScreenTransform != null)
            originalScreenParent = fpvScreenTransform.parent;

        // 1. Initialize texture buffer
        cameraTexture = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (screenRenderer != null)
        {
            screenRenderer.material.mainTexture = cameraTexture;
        }

        // Register Video 
        if (RosConnection_video == null)
            RosConnection_video = ROSConnection.GetOrCreateInstance();

        // Register General
        if (RosConnection_general == null)
            RosConnection_general = ROSConnection.GetOrCreateInstance();
            
        RosConnection_video.Subscribe<CompressedImageMsg>(imageTopic, OnImageReceived);
        RosConnection_general.Subscribe<Float32Msg>(tempTopic, msg => { cpuTemp = msg.data; });

        // Apply initial immersion mode
        SetMode(currentMode);
    }

    void Update()
    {
        // Safe keyboard checks using the New Input System
        bool modeKeyPressed = Keyboard.current != null && 
            (Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame);
        bool hudKeyPressed = Keyboard.current != null && 
            (Keyboard.current.tabKey.wasPressedThisFrame || Keyboard.current.hKey.wasPressedThisFrame);

        // Cycle Modes: Right Touch 'A' Button OR Keyboard '1' / 'Space'
        if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch) || modeKeyPressed)
        {
            currentMode = (ImmersionMode)(((int)currentMode + 1) % 3);
            SetMode(currentMode);
            Debug.Log($"<color=yellow>[Immersion Mode Switched]:</color> {currentMode}");
        }

        // Toggle HUD: Right Touch 'B' Button OR Keyboard 'Tab' / 'H'
        if (OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch) || hudKeyPressed)
        {
            if (debugCanvas != null)
            {
                debugCanvas.SetActive(!debugCanvas.activeSelf);
                Debug.Log($"<color=yellow>[HUD Toggled]:</color> Active={debugCanvas.activeSelf}");
            }
        }

        // Decode incoming JPEG frames and match aspect ratio
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
                float aspect = (float)cameraTexture.width / Mathf.Max(1, cameraTexture.height);
                Vector3 s = fpvScreenTransform.localScale;
                fpvScreenTransform.localScale = new Vector3(s.y * aspect, s.y, 1.0f);
            }
        }

        // Smooth billboard follow in SomeRobot mode
        if (currentMode == ImmersionMode.SomeRobot && centerEyeAnchor != null && fpvScreenTransform != null)
        {
            Vector3 targetPos = centerEyeAnchor.position + centerEyeAnchor.forward * 1.6f;
            fpvScreenTransform.position = Vector3.Lerp(fpvScreenTransform.position, targetPos, Time.deltaTime * 5f);
            fpvScreenTransform.rotation = Quaternion.Lerp(fpvScreenTransform.rotation, Quaternion.LookRotation(fpvScreenTransform.position - centerEyeAnchor.position), Time.deltaTime * 5f);
        }

        // Framerate calculation
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

    public void SetMode(ImmersionMode mode)
    {
        currentMode = mode;
        switch (mode)
        {
            case ImmersionMode.NoRobot:
                if (passthroughLayer != null) passthroughLayer.hidden = false;
                if (centerEyeCamera != null)
                {
                    centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                    centerEyeCamera.backgroundColor = new Color(0, 0, 0, 0);
                }
                if (screenRenderer != null) screenRenderer.gameObject.SetActive(false);
                break;

            case ImmersionMode.SomeRobot:
                if (passthroughLayer != null) passthroughLayer.hidden = false;
                if (centerEyeCamera != null)
                {
                    centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                    centerEyeCamera.backgroundColor = new Color(0, 0, 0, 0);
                }
                if (screenRenderer != null) screenRenderer.gameObject.SetActive(true);
                if (fpvScreenTransform != null)
                {
                    fpvScreenTransform.SetParent(originalScreenParent);
                    fpvScreenTransform.localScale = new Vector3(1.6f, 0.9f, 1.0f);
                }
                break;

            case ImmersionMode.InRobot:
                if (passthroughLayer != null) passthroughLayer.hidden = true;
                if (centerEyeCamera != null)
                {
                    centerEyeCamera.clearFlags = CameraClearFlags.SolidColor;
                    centerEyeCamera.backgroundColor = Color.black;
                }
                if (screenRenderer != null) screenRenderer.gameObject.SetActive(true);

                if (fpvScreenTransform != null && centerEyeAnchor != null)
                {
                    fpvScreenTransform.SetParent(centerEyeAnchor);
                    fpvScreenTransform.localPosition = new Vector3(0, 0, 1.0f);
                    fpvScreenTransform.localRotation = Quaternion.identity;
                    fpvScreenTransform.localScale = new Vector3(2.4f, 1.35f, 1.0f);
                }
                break;
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
            debugText.text = $"<b>SYSTEM DIAGNOSTICS (rqt)</b>\n" +
                             $"Stream FPS: {currentFps:F1}\n" +
                             $"Resolution: {cameraTexture.width}x{cameraTexture.height}\n" +
                             $"NUC CPU Temp: {cpuTemp:F1}°C\n" +
                             $"Mode: {currentMode}\n" +
                             $"Toggle Mode: [A Button] | Toggle HUD: [B Button]";
        }
    }
}