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

    // Interaction tracking
    private GameObject currentPressedObject = null;
    private GameObject currentDraggedObject = null;
    private GameObject currentHoveredObject = null;
    private PointerEventData currentPointerData = null;
    private Vector2 lastScreenPos = Vector2.zero;
    private bool isDragging = false;
    
    // Pixel travel needed before turning a click into a scroll drag
    public float dragThresholdPixels = 12.0f;

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

        // Input
        bool triggerPressed = OVRInput.Get(clickButton, controller) || DetectOpticalPinchHeld() || Input.GetMouseButton(0);
        bool triggerDown = OVRInput.GetDown(clickButton, controller) || DetectOpticalPinchDown() || Input.GetMouseButtonDown(0);
        bool triggerUp = OVRInput.GetUp(clickButton, controller) || DetectOpticalPinchUp() || Input.GetMouseButtonUp(0);

        // 1. Physics Hit Check
        bool hasPhysicsHit = Physics.Raycast(ray, out RaycastHit physicsHit, maxDistance, targetLayers);
        float closestDistance = hasPhysicsHit ? physicsHit.distance : maxDistance;
        Vector3 hitPoint = hasPhysicsHit ? physicsHit.point : ray.GetPoint(maxDistance);
        Vector3 hitNormal = hasPhysicsHit ? physicsHit.normal : -ray.direction;

        // 2. UI Raycast Check
        GameObject hitUIObject = null;
        RaycastResult topHitResult = default;

        if (currentPointerData == null)
        {
            currentPointerData = new PointerEventData(EventSystem.current)
            {
                pointerId = 1001
            };
        }

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

                PointerEventData tempPed = new PointerEventData(EventSystem.current) { position = screenPos };
                List<RaycastResult> results = new List<RaycastResult>();
                gr.Raycast(tempPed, results);

                if (results.Count > 0)
                {
                    closestDistance = enter;
                    hitPoint = candidatePoint;
                    hitNormal = canvas.transform.forward;
                    hitUIObject = results[0].gameObject;
                    topHitResult = results[0];
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

        // 3. UI Interactions
        Camera activeCam = uiCamera;
        Vector2 currentScreenPos = RectTransformUtility.WorldToScreenPoint(activeCam, hitPoint);
        
        currentPointerData.delta = currentScreenPos - lastScreenPos;
        currentPointerData.position = currentScreenPos;
        currentPointerData.pointerCurrentRaycast = topHitResult;
        lastScreenPos = currentScreenPos;

        // Hover handling
        if (hitUIObject != currentHoveredObject && !triggerPressed)
        {
            if (currentHoveredObject != null)
                ExecuteEvents.Execute(currentHoveredObject, currentPointerData, ExecuteEvents.pointerExitHandler);

            currentHoveredObject = hitUIObject;

            if (currentHoveredObject != null)
                ExecuteEvents.Execute(currentHoveredObject, currentPointerData, ExecuteEvents.pointerEnterHandler);
        }

        // Pointer Down
        if (triggerDown)
        {
            isDragging = false;
            currentPointerData.pressPosition = currentScreenPos;
            currentPointerData.pointerPressRaycast = topHitResult;
            currentPointerData.eligibleForClick = true;

            if (hitUIObject != null)
            {
                GameObject targetBtn = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitUIObject);
                GameObject targetDrag = ExecuteEvents.GetEventHandler<IDragHandler>(hitUIObject);

                // If on a button inside a scrollview, prioritize button unless scrolled
                if (targetDrag == null)
                {
                    ScrollRect sr = hitUIObject.GetComponentInParent<ScrollRect>();
                    if (sr != null) targetDrag = sr.gameObject;
                }

                currentPressedObject = targetBtn != null ? targetBtn : hitUIObject;
                currentPointerData.pointerPress = currentPressedObject;
                currentPointerData.rawPointerPress = hitUIObject;

                ExecuteEvents.Execute(currentPressedObject, currentPointerData, ExecuteEvents.pointerDownHandler);

                if (targetDrag != null)
                {
                    currentDraggedObject = targetDrag;
                    currentPointerData.pointerDrag = targetDrag;
                    ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.initializePotentialDrag);
                }
            }
        }

        // Dragging & Scrolling (Only if we actually pressed something scrollable)
        if (triggerPressed && currentDraggedObject != null)
        {
            float dragDistance = Vector2.Distance(currentPointerData.position, currentPointerData.pressPosition);

            if (!isDragging && dragDistance > dragThresholdPixels)
            {
                isDragging = true;
                currentPointerData.dragging = true;
                currentPointerData.eligibleForClick = false;

                ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.beginDragHandler);
            }

            if (isDragging)
            {
                ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.dragHandler);
            }
        }

        // Pointer Up
        if (triggerUp)
        {
            // End drag if we were dragging
            if (isDragging && currentDraggedObject != null)
            {
                ExecuteEvents.Execute(currentDraggedObject, currentPointerData, ExecuteEvents.endDragHandler);
            }

            // Resolve Pressed Object
            if (currentPressedObject != null)
            {
                // Always call pointer up to reset internal 'pressed' state
                ExecuteEvents.Execute(currentPressedObject, currentPointerData, ExecuteEvents.pointerUpHandler);

                // Fire click action only if we did NOT drag
                if (!isDragging && currentPointerData.eligibleForClick)
                {
                    Button btn = currentPressedObject.GetComponent<Button>();
                    if (btn != null && btn.interactable)
                    {
                        btn.onClick.Invoke();
                    }
                    else
                    {
                        ExecuteEvents.Execute(currentPressedObject, currentPointerData, ExecuteEvents.pointerClickHandler);
                    }
                }

                // Force button back to Idle/Hover visual state
                Selectable selectable = currentPressedObject.GetComponent<Selectable>();
                if (selectable != null)
                {
                    selectable.OnPointerExit(currentPointerData);
                    if (hitUIObject != null && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hitUIObject) == currentPressedObject)
                    {
                        selectable.OnPointerEnter(currentPointerData);
                    }
                }

                if (EventSystem.current != null)
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }

            // COMPLETE CLEANUP - prevents phantom drags anywhere on the screen
            isDragging = false;
            currentPointerData.dragging = false;
            currentPointerData.eligibleForClick = false;
            currentPointerData.pointerPress = null;
            currentPointerData.rawPointerPress = null;
            currentPointerData.pointerDrag = null;
            currentPressedObject = null;
            currentDraggedObject = null;
        }

        // Physics 3D Clicks
        if (hasPhysicsHit && triggerDown && hitUIObject == null)
        {
            IPointerClickHandler clickHandler = physicsHit.collider.GetComponent<IPointerClickHandler>();
            if (clickHandler != null)
            {
                PointerEventData eventData = new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(physicsHit.textureCoord.x, physicsHit.textureCoord.y)
                };
                clickHandler.OnPointerClick(eventData);
            }
        }

        bool isInteractive = (hitUIObject != null) || hasPhysicsHit;
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