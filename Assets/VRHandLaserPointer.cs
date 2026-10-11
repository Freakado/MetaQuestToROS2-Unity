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

    [Header("Interaction Configuration")]
    public Camera uiCamera;
    public float dragThresholdPixels = 12.0f;

    private LineRenderer lineRenderer;
    private GameObject reticleDot;
    private Transform reticleTransform;
    private Material laserMat;
    private Material reticleMat;

    private bool wasPinchingLastFrame = false;
    private Canvas[] worldCanvases;

    // Interaction tracking
    private GameObject currentPressedObject = null;
    private GameObject currentDraggedObject = null;
    private GameObject currentHoveredObject = null;
    private PointerEventData pointerData;
    private Vector2 lastScreenPos = Vector2.zero;
    private bool isDragging = false;

    // Cache to eliminate allocations in Update
    private readonly List<RaycastResult> cachedRaycastResults = new List<RaycastResult>(16);

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

        pointerData = new PointerEventData(EventSystem.current) { pointerId = 1001 };
        RefreshCanvases();
    }

    void OnEnable() => RefreshCanvases();

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

        // --- 1. Frame Input State (Fixed optical pinch evaluation) ---
        bool pinchHeld = DetectOpticalPinchHeld();
        bool pinchDown = pinchHeld && !wasPinchingLastFrame;
        bool pinchUp = !pinchHeld && wasPinchingLastFrame;
        wasPinchingLastFrame = pinchHeld;

        bool triggerPressed = OVRInput.Get(clickButton, controller) || pinchHeld || Input.GetMouseButton(0);
        bool triggerDown = OVRInput.GetDown(clickButton, controller) || pinchDown || Input.GetMouseButtonDown(0);
        bool triggerUp = OVRInput.GetUp(clickButton, controller) || pinchUp || Input.GetMouseButtonUp(0);

        // --- 2. Physics Hit Raycast ---
        bool hasPhysicsHit = Physics.Raycast(ray, out RaycastHit physicsHit, maxDistance, targetLayers);
        float closestDistance = hasPhysicsHit ? physicsHit.distance : maxDistance;
        Vector3 hitPoint = hasPhysicsHit ? physicsHit.point : ray.GetPoint(maxDistance);
        Vector3 hitNormal = hasPhysicsHit ? physicsHit.normal : -ray.direction;
        IPointerClickHandler physicsClickHandler = hasPhysicsHit ? physicsHit.collider.GetComponent<IPointerClickHandler>() : null;

        // --- 3. UI World Canvas Raycast ---
        GameObject hitUIObject = null;
        RaycastResult topHitResult = default;

        if (worldCanvases == null || worldCanvases.Length == 0) 
            RefreshCanvases();

        Camera activeCam = uiCamera != null ? uiCamera : Camera.main;

        for (int i = 0; i < worldCanvases.Length; i++)
        {
            Canvas canvas = worldCanvases[i];
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
                Camera cam = canvas.worldCamera != null ? canvas.worldCamera : activeCam;
                Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, candidatePoint);

                pointerData.position = screenPos;
                cachedRaycastResults.Clear();
                gr.Raycast(pointerData, cachedRaycastResults);

                if (cachedRaycastResults.Count > 0)
                {
                    closestDistance = enter;
                    hitPoint = candidatePoint;
                    hitNormal = canvas.transform.forward;
                    hitUIObject = cachedRaycastResults[0].gameObject;
                    topHitResult = cachedRaycastResults[0];
                }
            }
        }

        // --- 4. Render Laser & Reticle ---
        lineRenderer.SetPosition(0, originTransform.position);
        lineRenderer.SetPosition(1, hitPoint);

        bool hasHit = (closestDistance < maxDistance);
        reticleDot.SetActive(hasHit);
        if (hasHit)
        {
            reticleTransform.position = hitPoint - (ray.direction * 0.005f);
            reticleTransform.rotation = Quaternion.LookRotation(hitNormal);
        }

        // --- 5. UI Event Processing ---
        Vector2 currentScreenPos = RectTransformUtility.WorldToScreenPoint(activeCam, hitPoint);
        pointerData.delta = currentScreenPos - lastScreenPos;
        pointerData.position = currentScreenPos;
        pointerData.pointerCurrentRaycast = topHitResult;
        lastScreenPos = currentScreenPos;

        // Hover events
        if (hitUIObject != currentHoveredObject && !triggerPressed)
        {
            if (currentHoveredObject != null)
                ExecuteEvents.Execute(currentHoveredObject, pointerData, ExecuteEvents.pointerExitHandler);

            currentHoveredObject = hitUIObject;

            if (currentHoveredObject != null)
                ExecuteEvents.Execute(currentHoveredObject, pointerData, ExecuteEvents.pointerEnterHandler);
        }

        // Pointer Down
        if (triggerDown)
        {
            isDragging = false;
            pointerData.pressPosition = currentScreenPos;
            pointerData.pointerPressRaycast = topHitResult;
            pointerData.eligibleForClick = true;

            if (hitUIObject != null)
            {
                GameObject targetBtn = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitUIObject);
                GameObject targetDrag = ExecuteEvents.GetEventHandler<IDragHandler>(hitUIObject);

                if (targetDrag == null)
                {
                    ScrollRect sr = hitUIObject.GetComponentInParent<ScrollRect>();
                    if (sr != null) targetDrag = sr.gameObject;
                }

                currentPressedObject = targetBtn != null ? targetBtn : hitUIObject;
                pointerData.pointerPress = currentPressedObject;
                pointerData.rawPointerPress = hitUIObject;

                ExecuteEvents.Execute(currentPressedObject, pointerData, ExecuteEvents.pointerDownHandler);

                if (targetDrag != null)
                {
                    currentDraggedObject = targetDrag;
                    pointerData.pointerDrag = targetDrag;
                    ExecuteEvents.Execute(currentDraggedObject, pointerData, ExecuteEvents.initializePotentialDrag);
                }
            }
        }

        // Dragging & Scrolling
        if (triggerPressed && currentDraggedObject != null)
        {
            float dragDistance = Vector2.Distance(pointerData.position, pointerData.pressPosition);

            if (!isDragging && dragDistance > dragThresholdPixels)
            {
                isDragging = true;
                pointerData.dragging = true;
                pointerData.eligibleForClick = false;
                ExecuteEvents.Execute(currentDraggedObject, pointerData, ExecuteEvents.beginDragHandler);
            }

            if (isDragging)
            {
                ExecuteEvents.Execute(currentDraggedObject, pointerData, ExecuteEvents.dragHandler);
            }
        }

        // Pointer Up / Click
        if (triggerUp)
        {
            if (isDragging && currentDraggedObject != null)
            {
                ExecuteEvents.Execute(currentDraggedObject, pointerData, ExecuteEvents.endDragHandler);
            }

            if (currentPressedObject != null)
            {
                ExecuteEvents.Execute(currentPressedObject, pointerData, ExecuteEvents.pointerUpHandler);

                if (!isDragging && pointerData.eligibleForClick)
                {
                    ExecuteEvents.Execute(currentPressedObject, pointerData, ExecuteEvents.pointerClickHandler);
                }

                Selectable selectable = currentPressedObject.GetComponent<Selectable>();
                if (selectable != null)
                {
                    selectable.OnPointerExit(pointerData);
                    if (hitUIObject != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitUIObject) == currentPressedObject)
                    {
                        selectable.OnPointerEnter(pointerData);
                    }
                }

                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }

            // State reset
            isDragging = false;
            pointerData.dragging = false;
            pointerData.eligibleForClick = false;
            pointerData.pointerPress = null;
            pointerData.rawPointerPress = null;
            pointerData.pointerDrag = null;
            currentPressedObject = null;
            currentDraggedObject = null;
        }

        // --- 6. 3D Physics Clicks ---
        if (hasPhysicsHit && triggerDown && hitUIObject == null && physicsClickHandler != null)
        {
            physicsClickHandler.OnPointerClick(pointerData);
        }

        // --- 7. Laser Coloring ---
        bool isInteractive = (hitUIObject != null) || (hasPhysicsHit && physicsClickHandler != null);
        Color targetColor = (triggerPressed && isInteractive) ? clickColor : (isInteractive ? hoverColor : normalColor);
        laserMat.color = targetColor;
        reticleMat.color = targetColor;
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