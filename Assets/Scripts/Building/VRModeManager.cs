using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;

/// <summary>
/// VR front end for the builder. Two phases:
/// <list type="bullet">
/// <item><b>Tabletop (build)</b>: the XR Origin is scaled up, so the house reads as a
/// miniature model at waist height, seen from above like the desktop iso camera.
/// The world itself is never scaled, so all grid maths stays untouched.</item>
/// <item><b>Walk (interior)</b>: the rig returns to 1:1 inside the house and the XRI
/// Starter locomotion (move / turn / teleport / gravity) takes over.</item>
/// </list>
/// Bootstraps itself when the scene has an XR Origin and an XR loader is running;
/// without a headset the XR Origin is switched off and the desktop iso camera is used.
///
/// Controls — right trigger: build (same as a click/tap) · right grip: cancel ·
/// A: rotate · B: confirm · left menu: switch build / walk.
/// Tabletop only — left stick: pan · right stick ←/→: orbit 90° · right stick ↑/↓: zoom ·
/// X / Y: level down / up · right stick click: recenter.
/// </summary>
public class VRModeManager : MonoBehaviour
{
    public static VRModeManager Instance { get; private set; }
    public static bool Active => Instance != null && Instance.isActiveAndEnabled;

    [Header("Tabletop (build phase)")]
    public float startScale = 18f;
    public float minScale = 4f;
    public float maxScale = 40f;
    [Tooltip("Height of the model above the real floor, in player metres.")]
    public float tableHeight = 0.75f;
    [Tooltip("Horizontal distance from the head to the model centre, in player metres.")]
    public float viewDistance = 0.9f;
    public float zoomRate = 1.2f;
    [Tooltip("Pan speed in player metres per second.")]
    public float panSpeed = 0.6f;
    public float stickDeadzone = 0.25f;

    [Header("UI panel (rig space, player metres)")]
    public Vector3 uiLocalPos = new Vector3(-0.75f, 1.15f, 0.55f);
    public float uiYaw = -45f;
    public float uiPitch = 10f;
    public float uiWidth = 1.2f;

    XROrigin origin;
    Transform rig;
    Camera xrCam;
    GameObject locomotion;
    CharacterController rigCC;
    NearFarInteractor rightRay;
    Transform rightHand;
    readonly List<(CurveInteractionCaster caster, float baseDistance)> casters = new();
    float camNear, camFar;

    GridSystem grid;
    BuildManager bm;
    WalkController walk;

    bool tabletop;
    float scale = 1f;
    Vector3 pivot;
    float pivotLevelY;
    bool orbitLatched;

    InputAction trigger, grip, leftStick, rightStick, rightStickClick, btnA, btnB, btnX, btnY, menu;

    // ---------------------------------------------------------------- bootstrap

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // Runs after the scene's Awake but before any Start, so BuildManager /
        // WalkController see the headset camera when they cache Camera.main.
        var o = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (o == null || Instance != null) return;

        var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
        bool xrRunning = mgr != null && mgr.activeLoader != null;
        if (!xrRunning)
        {
            o.gameObject.SetActive(false); // desktop: keep the iso camera as the only camera
            return;
        }
        new GameObject("VRModeManager").AddComponent<VRModeManager>().Init(o);
    }

    void Init(XROrigin o)
    {
        Instance = this;
        origin = o;
        rig = o.transform;
        o.gameObject.SetActive(true);
        xrCam = o.Camera;
        camNear = xrCam.nearClipPlane;
        camFar = xrCam.farClipPlane;

        // Retire the desktop camera(s) so Camera.main resolves to the headset.
        foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (c != xrCam && c.CompareTag("MainCamera")) c.gameObject.SetActive(false);

        var loc = rig.Find("Locomotion");
        locomotion = loc != null ? loc.gameObject : null;
        rigCC = o.GetComponent<CharacterController>();

        foreach (var nf in o.GetComponentsInChildren<NearFarInteractor>(true))
            if (nf.handedness == InteractorHandedness.Right || (rightRay == null && nf.name.Contains("Right")))
                rightRay = nf;
        var rh = FindChildRecursive(rig, "Right Controller");
        rightHand = rh != null ? rh : (rightRay != null ? rightRay.transform : null);

        foreach (var c in o.GetComponentsInChildren<CurveInteractionCaster>(true))
            casters.Add((c, c.castDistance));

        CreateActions();
    }

    void CreateActions()
    {
        trigger = Button("<XRController>{RightHand}/{TriggerButton}");
        grip = Button("<XRController>{RightHand}/{GripButton}");
        btnA = Button("<XRController>{RightHand}/{PrimaryButton}");
        btnB = Button("<XRController>{RightHand}/{SecondaryButton}");
        btnX = Button("<XRController>{LeftHand}/{PrimaryButton}");
        btnY = Button("<XRController>{LeftHand}/{SecondaryButton}");
        menu = Button("<XRController>{LeftHand}/{MenuButton}");
        rightStickClick = Button("<XRController>{RightHand}/{Primary2DAxisClick}");
        leftStick = new InputAction(type: InputActionType.Value, binding: "<XRController>{LeftHand}/{Primary2DAxis}", expectedControlType: "Vector2");
        rightStick = new InputAction(type: InputActionType.Value, binding: "<XRController>{RightHand}/{Primary2DAxis}", expectedControlType: "Vector2");
        leftStick.Enable();
        rightStick.Enable();
    }

    static InputAction Button(string path)
    {
        var a = new InputAction(type: InputActionType.Button, binding: path);
        a.Enable();
        return a;
    }

    void Start()
    {
        grid = FindFirstObjectByType<GridSystem>();
        bm = FindFirstObjectByType<BuildManager>();
        walk = FindFirstObjectByType<WalkController>();

        pivot = grid != null
            ? grid.transform.position + new Vector3(grid.width * grid.cellSize * 0.5f, 0f, grid.height * grid.cellSize * 0.5f)
            : rig.position;
        pivotLevelY = pivot.y;
        scale = startScale;
        BeginTabletop();
        StartCoroutine(SetupWorldSpaceUI());
    }

    void OnDestroy()
    {
        foreach (var a in new[] { trigger, grip, leftStick, rightStick, rightStickClick, btnA, btnB, btnX, btnY, menu })
            a?.Dispose();
        if (Instance == this) Instance = null;
    }

    // ---------------------------------------------------------------- phases

    /// <summary>Build phase: giant rig over a miniature house.</summary>
    public void BeginTabletop()
    {
        tabletop = true;
        if (locomotion != null) locomotion.SetActive(false);
        if (rigCC != null) rigCC.enabled = false;
        Recenter();
    }

    /// <summary>Interior phase: 1:1 rig standing on <paramref name="floorPoint"/>.</summary>
    public void BeginWalk(Vector3 floorPoint)
    {
        tabletop = false;
        SetScale(1f);
        PlaceHead(floorPoint, floorPoint.y);
        if (rigCC != null) rigCC.enabled = true;
        if (locomotion != null) locomotion.SetActive(true);
    }

    /// <summary>The rig root (feet) — WalkController exposes this as the walker.</summary>
    public Transform RigTransform => rig;

    /// <summary>Put the model straight in front of the player at table height.</summary>
    void Recenter()
    {
        SyncPivotToLevel();
        SetScale(scale);
        float headYaw = xrCam.transform.eulerAngles.y;
        Vector3 fwd = Quaternion.Euler(0f, headYaw, 0f) * Vector3.forward;
        PlaceHead(pivot - fwd * (viewDistance * scale), pivot.y - tableHeight * scale);
    }

    /// <summary>Move the rig so the head lands above <paramref name="headXZ"/> and the
    /// rig floor sits at <paramref name="floorY"/>.</summary>
    void PlaceHead(Vector3 headXZ, float floorY)
    {
        Vector3 headOffset = xrCam.transform.position - rig.position;
        headOffset.y = 0f;
        rig.position = new Vector3(headXZ.x - headOffset.x, floorY, headXZ.z - headOffset.z);
    }

    void SetScale(float s)
    {
        rig.localScale = Vector3.one * s;
        xrCam.nearClipPlane = camNear * s;
        xrCam.farClipPlane = camFar * Mathf.Max(1f, s);
        foreach (var (caster, baseDistance) in casters)
            if (caster != null) caster.castDistance = baseDistance * s;
    }

    void SyncPivotToLevel()
    {
        if (grid == null || bm == null) return;
        float y = grid.transform.position.y + grid.LevelBaseY(bm.currentLevel);
        float dy = y - pivotLevelY;
        if (Mathf.Abs(dy) < 0.0001f) return;
        pivot.y += dy;
        pivotLevelY = y;
        rig.position += Vector3.up * dy; // ride up/down with the edited storey
    }

    // ---------------------------------------------------------------- input

    void Update()
    {
        if (menu.WasPressedThisFrame() && walk != null && (bm == null || bm.SessionStarted))
            walk.Toggle(); // WalkController calls back into BeginWalk / BeginTabletop

        if (bm != null && bm.SessionStarted)
        {
            if (btnA.WasPressedThisFrame()) bm.RotatePlacement();
            if (btnB.WasPressedThisFrame()) bm.ConfirmPlacement();
            if (grip.WasPressedThisFrame() && bm.ShowPlacementControls) bm.CancelPlacement();
        }

        if (!tabletop) return;

        float dt = Time.deltaTime;

        if (bm != null && bm.SessionStarted)
        {
            if (btnX.WasPressedThisFrame()) bm.StepLevel(-1);
            if (btnY.WasPressedThisFrame()) bm.StepLevel(1);
        }
        SyncPivotToLevel();

        if (rightStickClick.WasPressedThisFrame()) Recenter();

        // Right stick ←/→: orbit 90° around the model (instant snap = comfortable).
        Vector2 rs = rightStick.ReadValue<Vector2>();
        if (Mathf.Abs(rs.x) > 0.7f && !orbitLatched)
        {
            rig.RotateAround(pivot, Vector3.up, rs.x > 0f ? -90f : 90f);
            orbitLatched = true;
        }
        else if (Mathf.Abs(rs.x) < 0.3f) orbitLatched = false;

        // Right stick ↑/↓: zoom = rescale the rig around the model centre.
        if (Mathf.Abs(rs.y) > stickDeadzone && Mathf.Abs(rs.y) > Mathf.Abs(rs.x))
        {
            float newScale = Mathf.Clamp(scale * Mathf.Exp(-rs.y * zoomRate * dt), minScale, maxScale);
            float k = newScale / scale;
            if (!Mathf.Approximately(k, 1f))
            {
                rig.position = pivot + (rig.position - pivot) * k;
                scale = newScale;
                SetScale(scale);
            }
        }

        // Left stick: pan, relative to where the head is facing.
        Vector2 ls = leftStick.ReadValue<Vector2>();
        if (ls.sqrMagnitude > stickDeadzone * stickDeadzone)
        {
            Quaternion yawRot = Quaternion.Euler(0f, xrCam.transform.eulerAngles.y, 0f);
            Vector3 delta = yawRot * new Vector3(ls.x, 0f, ls.y) * (panSpeed * scale * dt);
            rig.position += delta;
            pivot += delta;
        }
    }

    /// <summary>Build pointer for BuildManager: right-hand ray + trigger.
    /// <paramref name="overUI"/> is true while the ray is on the menu panel.</summary>
    public bool TryGetBuildPointer(out Ray ray, out bool press, out bool pressedNow,
        out bool releasedNow, out bool cancelNow, out bool overUI)
    {
        ray = default;
        press = trigger.IsPressed();
        pressedNow = trigger.WasPressedThisFrame();
        releasedNow = trigger.WasReleasedThisFrame();
        cancelNow = grip.WasPressedThisFrame();
        overUI = rightRay != null && rightRay.TryGetCurrentUIRaycastResult(out _);

        Transform o = rightRay != null && rightRay.curveOrigin != null ? rightRay.curveOrigin : rightHand;
        if (o == null || !o.gameObject.activeInHierarchy) return false;
        ray = new Ray(o.position, o.forward);
        return true;
    }

    // ---------------------------------------------------------------- UI

    /// <summary>GameUI builds a Screen Space Overlay canvas, which a headset can't
    /// show. Turn it into a panel floating front-left of the player and let the
    /// controller rays click it.</summary>
    IEnumerator SetupWorldSpaceUI()
    {
        yield return null; // GameUI.Start has created its canvas and EventSystem by now

        var canvasGo = GameObject.Find("GameCanvas");
        if (canvasGo != null)
        {
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = xrCam;
            var rt = (RectTransform)canvas.transform;
            rt.SetParent(rig, false);
            rt.sizeDelta = new Vector2(1600f, 900f); // GameUI's CanvasScaler reference resolution
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localScale = Vector3.one * (uiWidth / 1600f);
            rt.localPosition = uiLocalPos;
            rt.localRotation = Quaternion.Euler(uiPitch, uiYaw, 0f);
            if (canvasGo.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
                canvasGo.AddComponent<TrackedDeviceGraphicRaycaster>();
        }

        var es = EventSystem.current;
        if (es != null && es.GetComponent<XRUIInputModule>() == null)
        {
            var old = es.GetComponent<InputSystemUIInputModule>();
            if (old != null) DestroyImmediate(old);
            var xrModule = es.gameObject.AddComponent<XRUIInputModule>();
            xrModule.enableXRInput = true;
            xrModule.enableMouseInput = true; // the desktop mirror stays clickable
        }
    }

    static Transform FindChildRecursive(Transform t, string name)
    {
        foreach (Transform c in t)
        {
            if (c.name == name) return c;
            var r = FindChildRecursive(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
