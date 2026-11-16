using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// First-person interior walkthrough. Toggle with F or the Walk button:
/// desktop = WASD move + hold RMB to look; touch = left 40% of the screen is a
/// virtual joystick, right side drags to look. Uses a CharacterController that
/// collides with the existing wall/floor/stair colliders, so stairs are walkable.
/// While walking the iso camera is disabled; exiting snaps it back.
/// </summary>
public class WalkController : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float eyeHeight = 1.55f;
    public float lookSpeed = 0.2f;

    GridSystem grid;
    BuildManager bm;
    IsoCameraController isoCam;
    Camera cam;
    CharacterController cc;
    GameObject walker;
    float yaw, pitchAngle, vy;
    Vector2 joyOrigin;

    public bool Walking { get; private set; }
    public Transform WalkerTransform => walker != null ? walker.transform : null;

    void Start()
    {
        grid = FindFirstObjectByType<GridSystem>();
        bm = FindFirstObjectByType<BuildManager>();
        isoCam = FindFirstObjectByType<IsoCameraController>();
        cam = Camera.main;
    }

    public void Toggle()
    {
        if (Walking) ExitWalk();
        else EnterWalk();
    }

    void EnterWalk()
    {
        if (Walking || grid == null || cam == null) return;
        int lv = bm != null ? bm.currentLevel : 0;

        // Entry rule (three states): ground floor always walkable; upper storeys
        // need floor tiles; anything else (empty levels, pure roof levels) refuses.
        if (lv > 0 && !grid.LevelHasFloor(lv))
        {
            if (GameUI.Instance != null) GameUI.Instance.ShowToast("No floor on this level to walk on");
            return;
        }

        Walking = true;
        if (bm != null) bm.SnapReveal(); // the complete house, instantly (like View)

        // Spawn: prefer a wall-enclosed interior point, but on upper storeys it
        // must land on a real floor tile (terraces have no walls); ground level
        // may fall back to the plot center (grass is fine to stand on).
        bool got = grid.TryGetInteriorSpawn(lv, out Vector3 spawn);
        if (lv > 0)
        {
            bool onFloor = got && grid.TryGetCellAt(spawn, out int scx, out int scz) && grid.HasFloor(scx, scz, lv);
            if (!onFloor) got = grid.TryGetFloorSpawn(lv, out spawn);
        }
        if (!got)
            spawn = grid.transform.position + new Vector3(
                grid.width * grid.cellSize * 0.5f,
                grid.LevelBaseY(lv),
                grid.height * grid.cellSize * 0.5f);
        spawn.y += 1.0f;
        walker = new GameObject("Walker");
        walker.transform.position = spawn;
        cc = walker.AddComponent<CharacterController>();
        cc.height = 1.7f;
        cc.radius = 0.28f;
        cc.center = new Vector3(0f, 0.85f, 0f);
        cc.stepOffset = 0.45f;
        cc.slopeLimit = 60f;
        yaw = cam.transform.eulerAngles.y;
        pitchAngle = 10f;
        vy = 0f;
        if (isoCam != null) isoCam.enabled = false;
        cam.orthographic = false;
        cam.fieldOfView = 70f;
    }

    public void ExitWalk()
    {
        if (!Walking) return;
        Walking = false;
        if (walker != null) Destroy(walker);
        if (isoCam != null)
        {
            isoCam.enabled = true;
            isoCam.ApplyTransform(true);
        }
    }

    void Update()
    {
        if (bm != null && !bm.SessionStarted) return; // main menu is up
        var kb = Keyboard.current;
        if (kb != null && kb.fKey.wasPressedThisFrame) Toggle();
        if (!Walking || walker == null) return;

        Vector2 look = Vector2.zero;
        Vector2 move = Vector2.zero;

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.isPressed) look = mouse.delta.ReadValue();
        if (kb != null)
        {
            if (kb.wKey.isPressed) move.y += 1f;
            if (kb.sKey.isPressed) move.y -= 1f;
            if (kb.dKey.isPressed) move.x += 1f;
            if (kb.aKey.isPressed) move.x -= 1f;
        }

        var ts = Touchscreen.current;
        if (ts != null)
        {
            foreach (var t in ts.touches)
            {
                if (!t.press.isPressed) continue;
                Vector2 pos = t.position.ReadValue();
                if (pos.x < Screen.width * 0.4f)
                {
                    if (t.press.wasPressedThisFrame) joyOrigin = pos;
                    Vector2 d = (pos - joyOrigin) / (Screen.height * 0.15f);
                    move += Vector2.ClampMagnitude(d, 1f);
                }
                else look += t.delta.ReadValue();
            }
        }

        yaw += look.x * lookSpeed;
        pitchAngle = Mathf.Clamp(pitchAngle - look.y * lookSpeed, -80f, 80f);

        Vector3 horiz = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y);
        if (horiz.sqrMagnitude > 1f) horiz.Normalize();
        vy = cc.isGrounded ? -1f : vy - 9.81f * Time.deltaTime;
        cc.Move((horiz * moveSpeed + Vector3.up * vy) * Time.deltaTime);

        cam.transform.position = walker.transform.position + Vector3.up * eyeHeight;
        cam.transform.rotation = Quaternion.Euler(pitchAngle, yaw, 0f);
    }
}
