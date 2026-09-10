using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using System.Collections;

public class RosConnectionManager : MonoBehaviour
{
    // --- The Singleton Pattern ---
    // This creates a single, publicly accessible instance of this manager.
    public static RosConnectionManager Instance { get; private set; }
    
    // --- The Public Ready Flag ---
    // Any script can check this to see if it's safe to publish.
    public static bool IsReady { get; private set; } = false;

    private ROSConnection ros;

    void Awake()
    {
        // --- Singleton Setup ---
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
        }
        else
        {
            Instance = this;
        }

        // --- Initialize ROS Connection ---
        ros = ROSConnection.GetOrCreateInstance();
        StartCoroutine(WaitForConnection());
    }

    private IEnumerator WaitForConnection()
    {
        // Wait until the background connection thread is running
        yield return new WaitUntil(() => ros.HasConnectionThread);

        // Wait a little extra time for the handshake to complete
        yield return new WaitForSeconds(0.5f);

        // Set the flag to true for all other scripts to see
        IsReady = true;
        Debug.Log("<color=green>ROS Connection is ready.</color>");
    }

    // When the application quits, make sure to reset the flag
    void OnApplicationQuit()
    {
        IsReady = false;
    }
}