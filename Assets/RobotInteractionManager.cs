using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

[System.Serializable]
public class ArtifactPayload
{
    public int tag_id;
    public string payload; // HTTP URL
}

[System.Serializable]
public class SolvedPayload
{
    public int tag_id;
}

public class RobotInteractionManager : MonoBehaviour
{

    [Header("ROS Connection Configuration")]
    [Tooltip("Dedicated high-bandwidth connection on Port 10001")]
    public ROSConnection RosConnection;

    [Header("ROS Topics")]
    public string artifactTopic = "/JeffCreate3/discovered_artifact";
    public string solvedTopic = "/JeffCreate3/puzzle_solved";
    public string exploreTopic = "/JeffCreate3/start_explore";
    public string systemCommandTopic = "/JeffCreate3/system_command";

    [Header("Artifact UI Popup References")]
    public GameObject artifactPopupPanel;
    public Text artifactTitleText;
    public Text artifactUrlText;
    public Button openBrowserButton;
    public Button dismissButton;

    [Header("System Command Palette UI")]
    public GameObject commandPalettePanel;

    private string currentActiveUrl = "";
    private int currentTagId = -1;

    void Start()
    {
        if (RosConnection == null)
            RosConnection = ROSConnection.GetOrCreateInstance();

        // Register publishers
        RosConnection.RegisterPublisher<StringMsg>(exploreTopic);
        RosConnection.RegisterPublisher<StringMsg>(systemCommandTopic);

        // Register subscribers
        RosConnection.Subscribe<StringMsg>(artifactTopic, OnArtifactDiscovered);
        RosConnection.Subscribe<StringMsg>(solvedTopic, OnPuzzleSolved);

        // Wire Button Listeners
        if (openBrowserButton != null)
            openBrowserButton.onClick.AddListener(OpenArtifactInBrowser);

        if (dismissButton != null)
            dismissButton.onClick.AddListener(DismissArtifactPopup);

        if (artifactPopupPanel != null)
            artifactPopupPanel.SetActive(false);
    }

    void Update()
    {
        // Toggle Command Palette with 'C' key or Left Touch 'X' Button
        bool toggleCommandPressed = (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) ||
                                     OVRInput.GetDown(OVRInput.Button.Three, OVRInput.Controller.LTouch);

        if (toggleCommandPressed && commandPalettePanel != null)
        {
            commandPalettePanel.SetActive(!commandPalettePanel.activeSelf);
        }

        // Quick shortcut to open browser popup if active: 'E' key
        if (artifactPopupPanel != null && artifactPopupPanel.activeSelf)
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
            {
                OpenArtifactInBrowser();
            }
        }
    }

    // --- ROS Callbacks ---

    private void OnArtifactDiscovered(StringMsg msg)
    {
        try
        {
            ArtifactPayload data = JsonUtility.FromJson<ArtifactPayload>(msg.data);
            if (data == null || string.IsNullOrEmpty(data.payload)) return;

            currentTagId = data.tag_id;
            currentActiveUrl = data.payload.Trim();

            // Run on main thread
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                if (artifactTitleText != null)
                    artifactTitleText.text = $"<b>ARTIFACT #{currentTagId} DETECTED</b>";

                if (artifactUrlText != null)
                    artifactUrlText.text = currentActiveUrl;

                if (artifactPopupPanel != null)
                    artifactPopupPanel.SetActive(true);
            });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ArtifactManager] JSON parse failed: {ex.Message}");
        }
    }

    private void OnPuzzleSolved(StringMsg msg)
    {
        try
        {
            SolvedPayload data = JsonUtility.FromJson<SolvedPayload>(msg.data);
            if (data != null && data.tag_id == currentTagId)
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    Debug.Log($"<color=green>Artifact #{data.tag_id} solved! Closing popup.</color>");
                    DismissArtifactPopup();
                });
            }
        }
        catch (Exception) { }
    }

    // --- UI Actions ---

    public void OpenArtifactInBrowser()
    {
        if (!string.IsNullOrEmpty(currentActiveUrl))
        {
            Debug.Log($"Launching URL: {currentActiveUrl}");
            Application.OpenURL(currentActiveUrl);
        }
    }

    public void DismissArtifactPopup()
    {
        if (artifactPopupPanel != null)
            artifactPopupPanel.SetActive(false);
        currentActiveUrl = "";
        currentTagId = -1;
    }

    // --- Command Palette Publishers ---

    public void PublishExploreCommand(string command)
    {
        StringMsg msg = new StringMsg(command);
        RosConnection.Publish(exploreTopic, msg);
        Debug.Log($"Sent Explore Command: '{command}' to {exploreTopic}");
    }

    public void PublishSystemCommand(string command)
    {
        StringMsg msg = new StringMsg(command);
        RosConnection.Publish(systemCommandTopic, msg);
        Debug.Log($"Sent System Command: '{command}' to {systemCommandTopic}");
    }
}