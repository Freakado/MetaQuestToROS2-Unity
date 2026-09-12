using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Std;

[System.Serializable]
public class ArtifactMsgData
{
    public int tag_id;
    public string payload; // HTTP URL
}

[System.Serializable]
public class SolvedMsgData
{
    public int tag_id;
}

public class RobotArtifactBrowser : MonoBehaviour
{

    [Header("ROS Connection Configuration")]
    [Tooltip("Dedicated high-bandwidth connection on Port 10001")]
    public ROSConnection RosConnection;

    [Header("ROS Topics")]
    public string artifactTopic = "/JeffCreate3/discovered_artifact";
    public string solvedTopic = "/JeffCreate3/puzzle_solved";

    [Header("In-World HUD Card References")]
    public GameObject cardPanel;         // ArtifactPopupPanel
    public Text titleText;               // ArtifactTitle
    public Text urlText;                 // ArtifactUrl
    public Button launchBrowserButton;   // Button to open web page
    public Button dismissButton;         // Button to close card

    private int activeTagId = -1;
    private string activeUrl = "";

    void Start()
    {
        if (cardPanel != null)
            cardPanel.SetActive(false);

        if (launchBrowserButton != null)
            launchBrowserButton.onClick.AddListener(OpenInWebBrowser);

        if (dismissButton != null)
            dismissButton.onClick.AddListener(Dismiss);

        // Register ROS Subscribers
        if (RosConnection == null)
            RosConnection = ROSConnection.GetOrCreateInstance();
            
        RosConnection.Subscribe<StringMsg>(artifactTopic, OnArtifactDiscovered);
        RosConnection.Subscribe<StringMsg>(solvedTopic, OnPuzzleSolved);
    }

    void Update()
    {
        // Keyboard fallback: press 'E' to open page, 'Escape' to dismiss
        if (cardPanel != null && cardPanel.activeSelf && Keyboard.current != null)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame)
                OpenInWebBrowser();

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
                Dismiss();
        }
    }

    private void OnArtifactDiscovered(StringMsg msg)
    {
        Debug.Log($"<color=cyan>[RECEIVER HIT]:</color> {msg.data}");

        try
        {
            ArtifactMsgData data = JsonUtility.FromJson<ArtifactMsgData>(msg.data);
            if (data == null)
            {
                Debug.LogError("[Artifact] Deserialization produced null data object.");
                return;
            }

            activeTagId = data.tag_id;
            activeUrl = data.payload != null ? data.payload.Trim() : "";

            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                Debug.Log($"<color=green>[ENQUEUED UI UPDATE]</color> Tag: {activeTagId}, URL: {activeUrl}");

                if (titleText != null)
                    titleText.text = $"ARTIFACT #{activeTagId} DETECTED";

                if (urlText != null)
                    urlText.text = activeUrl;

                if (cardPanel != null)
                    cardPanel.SetActive(true);
            });
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[Artifact] Error parsing payload: {ex.Message}");
        }
    }

    private void OnPuzzleSolved(StringMsg msg)
    {
        try
        {
            SolvedMsgData data = JsonUtility.FromJson<SolvedMsgData>(msg.data);
            if (data != null && data.tag_id == activeTagId)
            {
                UnityMainThreadDispatcher.Instance().Enqueue(() =>
                {
                    Debug.Log($"<color=green>[Puzzle Solved]</color> Tag #{data.tag_id} solved! Closing card.");
                    Dismiss();
                });
            }
        }
        catch (Exception) { }
    }

    public void OpenInWebBrowser()
    {
        if (!string.IsNullOrEmpty(activeUrl))
        {
            Debug.Log($"Launching system browser: {activeUrl}");
            Application.OpenURL(activeUrl);
        }
    }

    public void Dismiss()
    {
        if (cardPanel != null)
            cardPanel.SetActive(false);

        activeTagId = -1;
        activeUrl = "";
    }
}