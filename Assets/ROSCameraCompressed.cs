using UnityEngine;
using UnityEngine.UI;
using Unity.Robotics.ROSTCPConnector;
// The message type is now CompressedImageMsg
using RosMessageTypes.Sensor; 
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

public class RosCameraCompressed : MonoBehaviour
{
    public RawImage rawImageDisplay;
    // Change the topic name to the compressed topic
    public string imageTopic = "/image_raw/compressed";

    private ROSConnection ros;
    private Texture2D receivedTexture;
    private bool isTextureNew = false; 

    void Start()
    {
        receivedTexture = new Texture2D(1, 1);
        ros = ROSConnection.GetOrCreateInstance();
        
        // Make sure to subscribe with the CompressedImageMsg type
        ros.Subscribe<CompressedImageMsg>(imageTopic, ReceiveImageCallback);

        Debug.Log($"Subscribed to {imageTopic}");
    }

    void Update()
    {
        if (isTextureNew)
        {
            rawImageDisplay.texture = receivedTexture;
            isTextureNew = false;
        }
    }

    // The callback now receives a CompressedImageMsg
    private void ReceiveImageCallback(CompressedImageMsg message)
    {
        // The ToTexture2D() extension method also works on CompressedImageMsg
        receivedTexture = message.ToTexture2D();
        isTextureNew = true;
    }
}