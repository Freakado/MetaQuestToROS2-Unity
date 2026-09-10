using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

public class RosCameraSubscriberSkybox : MonoBehaviour
{
    // We no longer need a public material slot, as we'll modify the scene's global skybox.
    public string imageTopic = "/image_raw/compressed";
    private ROSConnection ros;
    private Texture2D receivedTexture;
    private bool isTextureNew = false;

    void Start()
    {
        receivedTexture = new Texture2D(1, 1);
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<CompressedImageMsg>(imageTopic, ReceiveImageCallback);
        Debug.Log($"Subscribed to {imageTopic}");

        // For safety, make sure a skybox is assigned on start.
        if (RenderSettings.skybox == null)
        {
            Debug.LogError("No Skybox Material is set in the scene's Lighting settings!");
            this.enabled = false;
        }
    }

    void Update()
    {
        if (isTextureNew)
        {
            // --- THIS IS THE KEY CHANGE ---
            // Instead of updating a specific material, we update the scene's active skybox.
            RenderSettings.skybox.mainTexture = receivedTexture;
            isTextureNew = false;
        }
    }

    private void ReceiveImageCallback(CompressedImageMsg message)
    {
        receivedTexture = message.ToTexture2D();
        isTextureNew = true;
    }
}