using UnityEngine;
// We no longer need UnityEngine.UI
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.Sensor;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

public class RosCameraSubscriber : MonoBehaviour
{
    // --- NEW: Public variable to link the Material in the Unity Editor ---
    public Material vrScreenMaterial;

    public string imageTopic = "/image_raw/compressed"; // Or your raw topic
    private ROSConnection ros;
    private Texture2D receivedTexture;
    private bool isTextureNew = false;

    void Start()
    {
        // Safety Check
        if (vrScreenMaterial == null)
        {
            Debug.LogError("VR Screen Material is not assigned! Please create and assign the material in the Inspector.");
            this.enabled = false;
            return;
        }

        // Clear the texture on start to prevent seeing the default grey
        vrScreenMaterial.mainTexture = null;

        receivedTexture = new Texture2D(1, 1);
        ros = ROSConnection.GetOrCreateInstance();
        ros.Subscribe<CompressedImageMsg>(imageTopic, ReceiveImageCallback);
        Debug.Log($"Subscribed to {imageTopic}");
    }

    void Update()
    {
        if (isTextureNew)
        {
            // --- NEW: Apply the texture to the material ---
            vrScreenMaterial.mainTexture = receivedTexture;
            isTextureNew = false;
        }
    }

    private void ReceiveImageCallback(CompressedImageMsg message)
    {
        receivedTexture = message.ToTexture2D();
        isTextureNew = true;
    }
}