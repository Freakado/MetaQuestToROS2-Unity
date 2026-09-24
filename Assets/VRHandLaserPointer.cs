using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(LineRenderer))]
public class VRHandLaserPointer : MonoBehaviour
{
    [Header("Mode & Manager")]
    public RobotImmersionManager immersionManager;
    public RobotImmersionManager.ImmersionMode activeInMode = RobotImmersionManager.ImmersionMode.SomeRobot;

    [Header("Raycast Settings")]
    public Transform customRayOrigin;
    public float maxDistance = 6.0f;
    public LayerMask targetLayers = ~0;

    [Header("Input Tracking")]
    public OVRInput.Controller controller = OVRInput.Controller.Active;
    public OVRInput.Button clickButton = OVRInput.Button.PrimaryIndexTrigger;
    public OVRHand ovrHand;

    [Header("Visuals")]
    public float beamWidth = 0.005f;
    public Color normalColor = new Color(0.2f, 0.7f, 1.0f, 0.6f);
    public Color hoverColor = new Color(0.1f, 1.0f, 0.4f, 0.9f);
    public Color clickColor = Color.white;

    private LineRenderer lineRenderer;
    private GameObject reticleDot;
    private Transform reticleTransform;
    private Material laserMat;
    private Material reticleMat;

    private bool wasPinchingLastFrame = false;
    private Canvas[] worldCanvases;
    public Camera uiCamera;

    // Drag & State tracking for ScrollRect
    private GameObject currentDraggedObject = null;
    private GameObject currentHoveredObject = null;
    private PointerEventData currentPointerData = null;
    private bool isDragging = false;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = beamWidth;
        lineRenderer.endWidth = beamWidth * 0.5f;
        lineRenderer.useWorldSpace = true;

        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        laserMat = new Material(unlitShader) { color = normalColor };
        lineRenderer.material = laserMat;

        reticleDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticleDot.name = "LaserPointerReticle";
        Destroy(reticleDot.GetComponent<Collider>());
        reticleTransform = reticleDot.transform;
        reticleTransform.localScale = Vector3.one * 0.015f;

        reticleMat = new Material(unlitShader) { color = normalColor };
        reticleDot.GetComponent<Renderer>().material = reticleMat;

        if (immersionManager == null)
            immersionManager = FindAnyObjectByType<RobotImmersionManager>();

        if (ovrHand == null)
            ovrHand = GetComponentInParent<OVRHand>();

        if (uiCamera == null)
        {
            var rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null && rig.centerEyeAnchor != null)
                uiCamera = rig.centerEyeAnchor.GetComponent<Camera>();
            if (uiCamera == null)
                uiCamera = Camera.main;
        }

        RefreshCanvases();
    }

    void OnEnable()
    {
        RefreshCanvases();
    }

    public void RefreshCanvases()
    {
        worldCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
    }

    void Update()
    {
        bool isModeActive = (immersionManager == null) || (immersionManager.currentMode == activeInMode);
        if (!isModeActive)
        {
            SetLaserVisible(false);
            wasPinchingLastFrame = false;
            return;
        }

        Transform originTransform = (customRayOrigin != null) ? customRayOrigin : transform;
        if (ovrHand != null && ovrHand.IsTracked && ovrHand.PointerPose != null)
        {
            originTransform = ovrHand.PointerPose;
        }

        SetLaserVisible(true);
        Ray ray = new Ray(originTransform.position, originTransform.forward);

        // Input Detection (Trigger / Pinch / Mouse)
        bool triggerPressed = OVRInput.Get(clickButton, controller) || DetectOpticalPinchHeld() || Input.GetMouseButton(0);
        bool triggerDown = OVRInput.GetDown(clickButton, controller) || DetectOpticalPinchDown() || Input.GetMouseButtonDown(0);
        bool triggerUp = OVRInput.GetUp(clickButton, controller) || DetectOpticalPinchUp() || Input.GetMouseButtonUp(0);

        // 1. Physics Hit (e.g., Colliders)
        bool hasPhysicsHit = Physics.Raycast(ray, out RaycastHit physicsHit, maxDistance, targetLayers);
        float closestDistance = hasPhysicsHit ? physicsHit.distance : maxDistance;
        Vector3 hitPoint = hasPhysicsHit ? physicsHit.point : ray.GetPoint(maxDistance);
        Vector3 hitNormal = hasPhysicsHit ? physicsHit.normal : -ray.direction;

        // 2. UI Raycast
        GameObject hitUIObject = null;
        if (worldCanvases == null || worldCanvases.Length == 0) RefreshCanvases();

        foreach (Canvas canvas in worldCanvases)
        {
            if (canvas == null || !canvas.gameObject.activeInHierarchy || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            GraphicRaycaster gr = canvas.GetComponent<GraphicRaycaster>();
            if (gr == null) continue;

            Plane plane = new Plane(canvas.transform.forward, canvas.transform.position);
            if (!plane.Raycast(ray, out float enter))
            {
                plane = new Plane(-canvas.transform.forward, canvas.transform.position);
                plane.Raycast(ray, out enter);
            }

            if (enter > 0 && enter < closestDistance)
            {
                Vector3 candidatePoint = ray.GetPoint(enter);
                Camera cam = canvas.worldCamera != null ? canvas.worldCamera : uiCamera;
                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, candidatePoint);

                PointerEventData ped = new PointerEventData(EventSystem.current) { position = screenPos };
                List<RaycastResult> results = new List<RaycastResult>();
                gr.Raycast(ped, results);

                if (results.Count > 0)
                {
                    closestDistance = enter;
                    hitPoint = candidatePoint;
                    hitNormal = canvas.transform.forward;
                    hitUIObject = results[0].gameObject;
                    currentPointerData = ped;
                    break;
                }
            }
        }

        // Draw Line & Reticle
        lineRenderer.SetPosition(0, originTransform.position);
        lineRenderer.SetPosition(1, hitPoint);

        bool hasHit = (closestDistance < maxDistance);
        reticleDot.SetActive(hasHit);
        if (hasHit)
        {
            reticleTransform.position = hitPoint - (ray.direction * 0.005f);
            reticleTransform.rotation = Quaternion.LookRotation(hitNormal);
        }

        // 3. UI Event Dispatching (Click, Hover, Drag / Scroll)
        bool isInteractive = (hitUIObject != null) || hasPhysicsHit;

        if (currentPointerData != null)
        {
            Camera cam = uiCamera;
            currentPointerData.position = RectTransformUtility.WorldToScreenPoint(cam, hitPoint);

            // Handle Pointer Enter / Exit
            if (hitUIObject != currentHoveredObject)
            {
                if (currentHoveredObject != null)
                    ExecuteEvents.Execute(currentHoveredObject, currentPointerData, ExecuteEvents.pointerExitHandler);

                currentHoveredObject = hitUIObject;

                if (currentHoveredObject != null)
                    ExecuteEvents.Execute(currentHoveredObject, currentPointerData, ExecuteEvents.pointerEnterHandler);
            }

            // Pointer Down
            if (triggerDown && hitUIObject != null)
            {
                currentPointerData.pressPosition = currentPointerData.position;
                currentPointerData.pointerPressRaycast = currentPointerData.pointerCurrentRaycast;

                // Find receiver for PointerDown
                GameObject target = ExecuteEvents.ExecuteHierarchy(hitUIObject, currentPointerData, ExecuteEvents.pointerDownHandler);
                if (target == null)
                    target = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitUIObject);

                currentPointerData.pointerPress = target;
                currentPointerData.rawPointerPress = hitUIObject;

                // Initialize Dragging on ScrollRect or Sliders
                GameObject dragHandler = ExecuteEvents.GetEventHandler<IDragHandler>(hitUIObject);
                if (dragHandler != null)
                {
                    currentDraggedObject = dragHandler;
                    ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.initializePotentialDrag);
                    isDragging = false;
                }
            }

            // Dragging (ScrollRect move)
            if (triggerPressed && currentDraggedObject != null)
            {
                if (!isDragging)
                {
                    ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.beginDragHandler);
                    isDragging = true;
                }
                ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.dragHandler);
            }

            // Pointer Up / Click
            if (triggerUp)
            {
                if (isDragging && currentDraggedObject != null)
                {
                    ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.endDragHandler);
                    isDragging = false;
                    currentDraggedObject = null;
                }

                if (currentPointerData.pointerPress != null)
                {
                    ExecuteEvents.Execute(currentPointerData.pointerPress, currentPointerData, ExecuteEvents.pointerUpHandler);

                    GameObject clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitUIObject);
                    if (clickTarget == currentPointerData.pointerPress)
                    {
                        ExecuteEvents.Execute(clickTarget, currentPointerData, ExecuteEvents.pointerClickHandler);
                    }
                    currentPointerData.pointerPress = null;
                }
            }
        }

        Color targetColor = (triggerPressed && isInteractive) ? clickColor : (isInteractive ? hoverColor : normalColor);
        laserMat.color = targetColor;
        reticleMat.color = targetColor;
    }

    private bool DetectOpticalPinchDown()
    {
        bool isPinchingNow = DetectOpticalPinchHeld();
        bool pinchDown = isPinchingNow && !wasPinchingLastFrame;
        wasPinchingLastFrame = isPinchingNow;
        return pinchDown;
    }

    private bool DetectOpticalPinchUp()
    {
        bool isPinchingNow = DetectOpticalPinchHeld();
        return !isPinchingNow && wasPinchingLastFrame;
    }

    private bool DetectOpticalPinchHeld()
    {
        if (ovrHand != null && ovrHand.IsTracked)
            return ovrHand.GetFingerIsPinching(OVRHand.HandFinger.Index);
        return OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RHand);
    }

    private void SetLaserVisible(bool visible)
    {
        if (lineRenderer.enabled != visible) lineRenderer.enabled = visible;
        if (reticleDot != null && reticleDot.activeSelf != visible) reticleDot.SetActive(visible);
    }

    void OnDestroy()
    {
        if (reticleDot != null) Destroy(reticleDot);
        if (laserMat != null) Destroy(laserMat);
        if (reticleMat != null) Destroy(reticleMat);
    }
}