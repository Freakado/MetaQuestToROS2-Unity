using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.RclInterfaces;

public class RosOutFeedController : MonoBehaviour
{
    [Header("ROS Connection")]
    public ROSConnection rosConnection;
    public string rosOutTopic = "/rosout";

    [Header("UI References")]
    [Tooltip("Target TextMeshProUGUI inside the ScrollRect Content")]
    public TextMeshProUGUI logTextDisplay;
    public ScrollRect logScrollRect;

    [Header("Buttons & Indicators")]
    public Button playPauseButton;
    public TextMeshProUGUI playPauseButtonText;
    public Image pauseStatusIndicator;

    [Header("Feed Settings")]
    [Tooltip("Total lines kept in history for scrolling")]
    public int maxHistory = 150;
    public bool showNodeName = true;
    public bool showTimestamp = true;

    [Header("Runtime State")]
    public bool isPaused = false;

    // Severity Levels from rcl_interfaces/msg/Log
    private const byte LOG_DEBUG = 10;
    private const byte LOG_INFO = 20;
    private const byte LOG_WARN = 30;
    private const byte LOG_ERROR = 40;
    private const byte LOG_FATAL = 50;

    private readonly List<string> displayedLogs = new List<string>();
    private readonly Queue<string> pausedBacklog = new Queue<string>();
    private readonly object lockObject = new object();
    private bool uiDirty = false;

    void Awake()
    {
        if (rosConnection == null)
        {
            ROSConnection[] connections = FindObjectsByType<ROSConnection>(FindObjectsSortMode.None);
            foreach (var conn in connections)
            {
                if (conn.RosPort == 10000 || conn.gameObject.name.ToLower().Contains("control") || conn.gameObject.name.ToLower().Contains("general"))
                {
                    rosConnection = conn;
                    break;
                }
            }
            if (rosConnection == null)
                rosConnection = ROSConnection.GetOrCreateInstance();
        }
    }

    void Start()
    {
        if (rosConnection != null)
        {
            rosConnection.Subscribe<LogMsg>(rosOutTopic, OnRosOutReceived);
            AppendManualNotice("<color=#888888>[SYS] Subscribed to " + rosOutTopic + "</color>");
        }

        if (playPauseButton != null)
        {
            playPauseButton.onClick.AddListener(TogglePause);
        }

        UpdatePauseUI();
    }

    void Update()
    {
        if (uiDirty)
        {
            FlushToUI();
        }
    }

    private void OnRosOutReceived(LogMsg msg)
    {
        // Filter out unity_endpoint self-registration echo chatter
        if (msg.name.Contains("unity_endpoint") && msg.msg.Contains("Publisher already registered"))
            return;

        string formattedEntry = FormatLogMessage(msg);

        lock (lockObject)
        {
            if (isPaused)
            {
                // Buffer incoming logs so they appear when unpaused
                pausedBacklog.Enqueue(formattedEntry);
            }
            else
            {
                AddMessageToHistory(formattedEntry);
                uiDirty = true;
            }
        }
    }

    private void AddMessageToHistory(string entry)
    {
        if (displayedLogs.Count >= maxHistory)
        {
            displayedLogs.RemoveAt(0);
        }
        displayedLogs.Add(entry);
    }

    private string FormatLogMessage(LogMsg msg)
    {
        string levelTag;
        string colorHex;

        switch (msg.level)
        {
            case LOG_DEBUG:
                levelTag = "DBG";
                colorHex = "#9E9E9E";
                break;
            case LOG_INFO:
                levelTag = "INF";
                colorHex = "#4CAF50";
                break;
            case LOG_WARN:
                levelTag = "WRN";
                colorHex = "#FFC107";
                break;
            case LOG_ERROR:
                levelTag = "ERR";
                colorHex = "#F44336";
                break;
            case LOG_FATAL:
                levelTag = "FTL";
                colorHex = "#E91E63";
                break;
            default:
                levelTag = "LOG";
                colorHex = "#FFFFFF";
                break;
        }

        string timeStr = showTimestamp ? $"<color=#777777>[{DateTime.Now:HH:mm:ss}]</color> " : "";
        string cleanName = msg.name.Replace("/JeffCreate3/", "");
        string nodeStr = showNodeName ? $"<color=#64B5F6>[{cleanName}]</color> " : "";

        return $"{timeStr}<color={colorHex}>[{levelTag}]</color> {nodeStr}{msg.msg}";
    }

    private void AppendManualNotice(string notice)
    {
        lock (lockObject)
        {
            AddMessageToHistory(notice);
            uiDirty = true;
        }
    }

    private void FlushToUI()
    {
        string fullText;
        lock (lockObject)
        {
            fullText = string.Join("\n", displayedLogs);
            uiDirty = false;
        }

        if (logTextDisplay != null)
        {
            logTextDisplay.text = fullText;
        }

        // Auto-scroll to bottom only while live playback is running
        if (!isPaused && logScrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            logScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    public void TogglePause()
    {
        lock (lockObject)
        {
            isPaused = !isPaused;

            // When resuming play mode, dump all accumulated backlog into the visible view
            if (!isPaused)
            {
                while (pausedBacklog.Count > 0)
                {
                    AddMessageToHistory(pausedBacklog.Dequeue());
                }
                uiDirty = true;
            }
        }

        UpdatePauseUI();
    }

    public void ClearLogs()
    {
        lock (lockObject)
        {
            displayedLogs.Clear();
            pausedBacklog.Clear();
            uiDirty = true;
        }
    }

    private void UpdatePauseUI()
    {
        if (playPauseButtonText != null)
        {
            playPauseButtonText.text = isPaused ? "RESUME" : "PAUSE";
        }

        if (pauseStatusIndicator != null)
        {
            pauseStatusIndicator.color = isPaused ? new Color(1f, 0.75f, 0.1f, 1f) : new Color(0.2f, 0.8f, 0.2f, 1f);
        }
    }
}