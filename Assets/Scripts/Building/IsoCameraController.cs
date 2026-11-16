using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Monument-Valley-style camera: orthographic, 30° pitch, yaw stepping in 90°
/// increments (Q/E), WASD/arrow pan relative to view, scroll zoom, MMB drag pan.
/// The camera orbits a pivot; panning moves the pivot.
/// </summary>
[RequireComponent(typeof(Camera))]
public class IsoCameraController : MonoBehaviour
{
    public Transform pivot;
    public float pitch = 30f;
    public float distance = 30f;
    public float minOrthoSize = 2.5f;
    public float maxOrthoSize = 16f;
    public float startOrthoSize = 9f;
    public float panSpeed = 8f;

    Camera cam;
    float targetYaw = 45f;
    float currentYaw = 45f;
    float targetSize;

    /// <summary>Bird's-eye: straight-down 90° pitch for floorplan-style viewing/editing.</summary>
    public bool birdView;
    [System.NonSerialized] float currentPitch;
    [System.NonSerialized] float targetPitch;
    [System.NonSerialized] float savedIsoYaw = 45f;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        targetSize = startOrthoSize;
        cam.orthographicSize = startOrthoSize;
        ApplyTransform(true);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        var kb = Keyboard.current;
        var mouse = Mouse.current;

        if (kb != null)
        {
            if (kb.qKey.wasPressedThisFrame) RotateStep(-1);
            if (kb.eKey.wasPressedThisFrame) RotateStep(1);
            if (kb.vKey.wasPressedThisFrame) ToggleBirdView();

            Vector3 dirInput = Vector3.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dirInput.z += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dirInput.z -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dirInput.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dirInput.x -= 1f;
            if (dirInput.sqrMagnitude > 0f && pivot != null)
            {
                var yawRot = Quaternion.Euler(0f, currentYaw, 0f);
                pivot.position += yawRot * dirInput.normalized * (panSpeed * (cam.orthographicSize / startOrthoSize) * dt);
            }
        }

        float zoomDelta = 0f;
        if (kb != null)
        {
            if (kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed) zoomDelta += 1.5f * dt;
            if (kb.minusKey.isPressed || kb.numpadMinusKey.isPressed) zoomDelta -= 1.5f * dt;
        }
        if (mouse != null)
        {
            float scroll = Mathf.Clamp(mouse.scroll.ReadValue().y, -3f, 3f);
            if (Mathf.Abs(scroll) > 0.01f) zoomDelta += scroll * 0.08f;

            if (mouse.middleButton.isPressed && pivot != null)
            {
                Vector2 delta = mouse.delta.ReadValue();
                float unitsPerPixel = cam.orthographicSize * 2f / Screen.height;
                var yawRot = Quaternion.Euler(0f, currentYaw, 0f);
                pivot.position -= yawRot * new Vector3(delta.x, 0f, delta.y) * unitsPerPixel;
            }
        }

        if (zoomDelta != 0f)
            targetSize = Mathf.Clamp(targetSize * (1f - zoomDelta), minOrthoSize, maxOrthoSize);

        // Two-finger touch: pinch zoom + drag pan (build input ignores multi-touch)
        var ts = Touchscreen.current;
        if (ts != null)
        {
            Vector2 p0 = default, p1 = default, d0 = default, d1 = default;
            int n = 0;
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                if (n == 0) { p0 = t.position.ReadValue(); d0 = t.delta.ReadValue(); }
                else if (n == 1) { p1 = t.position.ReadValue(); d1 = t.delta.ReadValue(); }
                n++;
            }
            if (n >= 2)
            {
                float distNow = Vector2.Distance(p0, p1);
                float distPrev = Vector2.Distance(p0 - d0, p1 - d1);
                if (distNow > 1f && distPrev > 1f)
                    targetSize = Mathf.Clamp(targetSize * (distPrev / distNow), minOrthoSize, maxOrthoSize);
                if (pivot != null)
                {
                    Vector2 avg = (d0 + d1) * 0.5f;
                    float unitsPerPixel = cam.orthographicSize * 2f / Screen.height;
                    var yawRot = Quaternion.Euler(0f, currentYaw, 0f);
                    pivot.position -= yawRot * new Vector3(avg.x, 0f, avg.y) * unitsPerPixel;
                }
            }
        }

        float k = 1f - Mathf.Exp(-10f * dt);
        if (hasFocusTarget && pivot != null)
        {
            var pp = pivot.position;
            pp.y = Mathf.Lerp(pp.y, targetFocusY, k);
            pivot.position = pp;
        }
        currentYaw = Mathf.LerpAngle(currentYaw, targetYaw, k);
        currentPitch = Mathf.Lerp(currentPitch, targetPitch, k);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, k);
        ApplyTransform(false);
    }

    [System.NonSerialized] float targetFocusY;
    [System.NonSerialized] bool hasFocusTarget;

    public void RotateStep(int dir) => targetYaw += 90f * dir;

    /// <summary>Pan the pivot by a screen-space pixel delta (one-finger drag / MMB).</summary>
    public void PanByScreenDelta(Vector2 delta)
    {
        if (pivot == null || cam == null) return;
        float unitsPerPixel = cam.orthographicSize * 2f / Screen.height;
        var yawRot = Quaternion.Euler(0f, currentYaw, 0f);
        pivot.position -= yawRot * new Vector3(delta.x, 0f, delta.y) * unitsPerPixel;
    }

    public void ZoomBy(float factor) =>
        targetSize = Mathf.Clamp(targetSize * factor, minOrthoSize, maxOrthoSize);

    public void ToggleBirdView()
    {
        birdView = !birdView;
        if (birdView)
        {
            // Plan view reads best axis-aligned: swing yaw to the nearest 90° multiple
            savedIsoYaw = targetYaw;
            targetYaw = Mathf.Round(targetYaw / 90f) * 90f;
            targetPitch = 90f;
        }
        else
        {
            targetYaw = savedIsoYaw;
            targetPitch = pitch;
        }
    }

    /// <summary>Raise/lower the orbit pivot to follow the floor being edited (smoothed in Update).</summary>
    public void SetFocusHeight(float y, bool snap = false)
    {
        targetFocusY = y;
        hasFocusTarget = true;
        if (snap && pivot != null)
            pivot.position = new Vector3(pivot.position.x, y, pivot.position.z);
    }

    public void ApplyTransform(bool snap)
    {
        if (pivot == null) return;
        if (cam == null) cam = GetComponent<Camera>();
        if (snap)
        {
            currentYaw = targetYaw;
            targetPitch = birdView ? 90f : pitch;
            currentPitch = targetPitch;
            targetSize = startOrthoSize;
            cam.orthographic = true;
            cam.orthographicSize = startOrthoSize;
        }
        var rot = Quaternion.Euler(currentPitch, currentYaw, 0f);
        transform.rotation = rot;
        transform.position = pivot.position - rot * Vector3.forward * distance;
    }
}
