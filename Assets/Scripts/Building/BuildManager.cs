using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

/// <summary>
/// Tool-mode building: raycast the mouse onto the current level's plane, snap to
/// grid, show a ghost preview, place/erase. Floors use rectangle drag; walls use
/// drag painting; door/window use single clicks. [ ] / PgUp / PgDn change level;
/// levels above the current one are hidden (cutaway view).
/// Touch mapping comes later; the prototype drives with mouse + keyboard.
/// </summary>
public class BuildManager : MonoBehaviour
{
    public enum Tool { Wall, Floor, Erase, Door, Window, Stair, Roof, Furniture }

    public GridSystem grid;
    public Tool tool = Tool.Wall;
    public int currentLevel;

    Camera cam;
    IsoCameraController camController;
    Transform ghost;
    MeshRenderer ghostRenderer;
    Material ghostOkMat, ghostBadMat;

    bool floorDragging;
    Vector2Int dragStart;
    RectInt dragRect;

    public float flyDuration = 0.3f;
    bool overviewMode;
    float[] reveal;
    float gridLineY;

    int stairRot;
    Transform stairGhost;
    MeshRenderer[] stairGhostRenderers;

    Transform roofGhost;
    int roofFootprintHash;
    int roofFootprintLevel = -1;

    public string saveSlot = "building_story";
    string autoSlot = "autosave_story";
    public bool SessionStarted { get; private set; }
    public bool FreeMode { get; private set; }

    FurnitureItem selectedFurniture;
    int furnRot;
    string selectedFloorStyle; // empty = lay default style, never repaint
    string selectedWallStyle;   // empty = build default style, never repaint
    string selectedDoorStyle;   // per-kind catalogs: door walls pick door styles
    string selectedWindowStyle;
    WalkController walk;

    // Confirm-flow placement (furniture & stairs): tap parks the ghost, the
    // Rotate/Confirm/Cancel buttons drive it; nothing is placed until Confirm.
    bool hasPendingFurn;
    int pendingSx, pendingSz;
    bool hasPendingStair;
    Vector2Int pendingStairCell;

    // Single-pointer gesture: short press = tap (build), long move = camera pan
    bool gestureActive, gestureIsPan;
    Vector2 gestureStartPos, lastPointerPos;

    // Hold-to-erase locks onto the first category hit, so deleting furniture
    // can't cascade into the floor beneath it on the next held frame.
    enum EraseKind { None, Wall, Stair, Furniture, Roof, Floor }
    EraseKind eraseLock;
    bool Allow(EraseKind k) => eraseLock == EraseKind.None || eraseLock == k;

    // Walk mode: quick taps on the look side of the screen build; drags look around.
    Vector2 walkTapStart;
    float walkTapTime;
    bool walkTapValid;

    void Start()
    {
        cam = Camera.main;
        camController = FindFirstObjectByType<IsoCameraController>();
        walk = FindFirstObjectByType<WalkController>();
        if (grid == null) grid = FindFirstObjectByType<GridSystem>();

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "PlacementGhost";
        Destroy(go.GetComponent<Collider>());
        ghost = go.transform;
        ghostRenderer = go.GetComponent<MeshRenderer>();
        ghostOkMat = CreateGhostMaterial(new Color(0.30f, 0.90f, 0.50f, 0.45f));
        ghostBadMat = CreateGhostMaterial(new Color(0.95f, 0.35f, 0.30f, 0.45f));
        ghost.gameObject.SetActive(false);

        if (grid != null)
        {
            var sg = new GameObject("StairGhost");
            grid.BuildStairChildren(sg.transform, ghostOkMat);
            foreach (var col in sg.GetComponentsInChildren<Collider>()) Destroy(col);
            stairGhostRenderers = sg.GetComponentsInChildren<MeshRenderer>();
            stairGhost = sg.transform;
            sg.SetActive(false);
        }

        reveal = new float[grid != null ? Mathf.Max(1, grid.maxLevels) : 1];
        ApplyLevel(true);
        // Loading waits for the main menu: BeginSession() picks the mode's save.
    }

    /// <summary>Enter story or free mode from the main menu; each mode keeps its
    /// own building saves.</summary>
    public void BeginSession(bool freeMode)
    {
        FreeMode = freeMode;
        saveSlot = freeMode ? "building_free" : "building_story";
        autoSlot = freeMode ? "autosave_free" : "autosave_story";
        if (grid != null && BuildingSerializer.LoadFromFile(grid, autoSlot))
            ShowToast("Progress restored");
        currentLevel = 0;
        overviewMode = false;
        ApplyLevel(true);
        SessionStarted = true;
    }

    /// <summary>Back to the main menu: save this mode's world and pause input.</summary>
    public void EndSession()
    {
        if (!SessionStarted) return;
        if (grid != null) BuildingSerializer.SaveToFile(grid, autoSlot);
        CancelPlacement();
        SessionStarted = false;
    }

    void OnDestroy()
    {
        if (SessionStarted && grid != null) BuildingSerializer.SaveToFile(grid, autoSlot);
    }

    void ShowToast(string msg)
    {
        if (GameUI.Instance != null) GameUI.Instance.ShowToast(msg);
    }

    // ---- Public API shared by keyboard shortcuts and UI buttons ----

    public void SetTool(Tool t) => tool = t;

    /// <summary>True while the Rotate/Confirm/Cancel buttons should be visible.
    /// Stays up during walk (placing furniture in first person is allowed);
    /// hidden in view mode, which suppresses all building.</summary>
    public bool ShowPlacementControls =>
        !overviewMode
        && ((tool == Tool.Furniture && selectedFurniture != null) || tool == Tool.Stair);

    public void RotatePlacement()
    {
        if (tool == Tool.Furniture)
        {
            if (selectedFurniture != null && hasPendingFurn)
            {
                // rotate around the footprint center so the ghost stays put
                var oldFpt = GridSystem.FurnFootprint(selectedFurniture, furnRot);
                float s = grid != null ? grid.SubCell : 0.25f;
                float cx = pendingSx * s + oldFpt.x * 0.5f;
                float cz = pendingSz * s + oldFpt.y * 0.5f;
                furnRot = (furnRot + 1) & 3;
                var newFpt = GridSystem.FurnFootprint(selectedFurniture, furnRot);
                pendingSx = Mathf.RoundToInt((cx - newFpt.x * 0.5f) / s);
                pendingSz = Mathf.RoundToInt((cz - newFpt.y * 0.5f) / s);
            }
            else furnRot = (furnRot + 1) & 3;
        }
        else stairRot = (stairRot + 1) & 3;
        RefreshPendingGhost();
    }

    public void ConfirmPlacement()
    {
        if (grid == null) return;
        if (tool == Tool.Furniture && selectedFurniture != null && hasPendingFurn)
        {
            string error = grid.GetFurniturePlacementError(selectedFurniture, pendingSx, pendingSz, furnRot, currentLevel);
            if (error == null)
            {
                grid.PlaceFurniture(selectedFurniture, pendingSx, pendingSz, furnRot, currentLevel);
                hasPendingFurn = false;
                HideGhost();
                ShowToast(selectedFurniture.DisplayName + " placed.");
            }
            else ShowToast(error);
        }
        else if (tool == Tool.Furniture && selectedFurniture != null)
            ShowToast("Tap a location first.");
        else if (tool == Tool.Stair && hasPendingStair)
        {
            if (grid.CanPlaceStair(pendingStairCell.x, pendingStairCell.y, stairRot, currentLevel))
            {
                grid.PlaceStair(pendingStairCell.x, pendingStairCell.y, stairRot, currentLevel);
                hasPendingStair = false;
                HideStairGhost();
            }
            else ShowToast("Doesn't fit here");
        }
    }

    public void CancelPlacement()
    {
        hasPendingFurn = false;
        hasPendingStair = false;
        if (tool == Tool.Furniture) selectedFurniture = null;
        else if (tool == Tool.Stair) tool = Tool.Wall;
        HideGhost();
        HideStairGhost();
    }

    /// <summary>Redraw the parked ghost from pending state (also called from the
    /// Rotate button, when no pointer event would refresh it).</summary>
    void RefreshPendingGhost()
    {
        if (grid == null) return;
        if (tool == Tool.Furniture && selectedFurniture != null && hasPendingFurn)
        {
            var fpt = GridSystem.FurnFootprint(selectedFurniture, furnRot);
            float s = grid.SubCell;
            bool ok = grid.CanPlaceFurniture(selectedFurniture, pendingSx, pendingSz, furnRot, currentLevel);
            Vector3 center = grid.transform.TransformPoint(new Vector3(
                pendingSx * s + fpt.x * 0.5f,
                grid.LevelBaseY(currentLevel) + 0.12f + selectedFurniture.size.y * 0.5f,
                pendingSz * s + fpt.y * 0.5f));
            ShowGhost(center, new Vector3(fpt.x, selectedFurniture.size.y, fpt.y) * 1.02f, ok);
        }
        else if (tool == Tool.Stair && hasPendingStair)
        {
            bool ok = grid.CanPlaceStair(pendingStairCell.x, pendingStairCell.y, stairRot, currentLevel);
            ShowStairGhost(grid.StairCenterWorld(pendingStairCell.x, pendingStairCell.y, stairRot, currentLevel),
                GridSystem.StairYaw(stairRot), ok);
        }
    }

    public void SelectFurniture(FurnitureItem item)
    {
        // A new catalog choice starts a fresh preview, never confirms the old footprint.
        hasPendingFurn = false;
        HideGhost();
        selectedFurniture = item;
        tool = Tool.Furniture;
    }

    public void SelectFloorStyle(string styleName)
    {
        selectedFloorStyle = styleName;
        tool = Tool.Floor;
    }

    public void SelectWallStyle(WallOpening kind, string styleName)
    {
        if (kind == WallOpening.Door) { selectedDoorStyle = styleName; tool = Tool.Door; }
        else if (kind == WallOpening.Window) { selectedWindowStyle = styleName; tool = Tool.Window; }
        else { selectedWallStyle = styleName; tool = Tool.Wall; }
    }
    public bool OverviewOn => overviewMode;
    public string FurniturePlacementHint
    {
        get
        {
            if (!SessionStarted || !ShowPlacementControls || tool != Tool.Furniture || grid == null) return "";
            if (!hasPendingFurn) return "Tap a location to preview furniture.";
            return grid.GetFurniturePlacementError(selectedFurniture, pendingSx, pendingSz, furnRot, currentLevel)
                ?? "Ready: rotate or confirm placement.";
        }
    }
    public bool DraggingRect => floorDragging;
    public RectInt DragRect => dragRect;

    public void StepLevel(int delta)
    {
        if (walk != null && walk.Walking) return; // walking sees the whole house
        currentLevel += delta;
        overviewMode = false;
        ApplyLevel();
    }

    /// <summary>Instantly apply the current reveal targets (walk entry wants the
    /// complete house immediately, like toggling View).</summary>
    public void SnapReveal() => ApplyLevel(true);

    public void ToggleOverviewMode()
    {
        overviewMode = !overviewMode;
        if (grid != null) grid.SetGridLinesVisible(!overviewMode);
    }

    public void SaveGame()
    {
        if (grid == null) return;
        BuildingSerializer.SaveToFile(grid, saveSlot);
        ShowToast("Saved");
    }

    public void LoadGame()
    {
        if (grid == null) return;
        if (BuildingSerializer.LoadFromFile(grid, saveSlot)) { ApplyLevel(true); ShowToast("Loaded"); }
        else ShowToast("No save file");
    }

    float TargetReveal(int level) =>
        (overviewMode || (walk != null && walk.Walking) || level <= currentLevel) ? 1f : 0f;

    void ApplyLevel(bool snap = false)
    {
        if (grid == null) return;
        currentLevel = Mathf.Clamp(currentLevel, 0, grid.maxLevels - 1);
        if (camController != null)
            camController.SetFocusHeight(grid.transform.position.y + grid.LevelBaseY(currentLevel), snap);
        if (snap)
        {
            for (int i = 0; i < reveal.Length; i++)
            {
                reveal[i] = TargetReveal(i);
                grid.ApplyLevelReveal(i, reveal[i]);
            }
            gridLineY = grid.LevelBaseY(currentLevel);
            grid.SetGridLinesHeight(gridLineY);
            grid.SetGridLinesVisible(!overviewMode);
        }
        // Non-snap changes animate toward their targets in Update.
    }

    void Update()
    {
        if (!SessionStarted) return; // main menu is up

        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.digit1Key.wasPressedThisFrame) tool = Tool.Wall;
            if (kb.digit2Key.wasPressedThisFrame) tool = Tool.Floor;
            if (kb.digit3Key.wasPressedThisFrame) tool = Tool.Erase;
            if (kb.digit4Key.wasPressedThisFrame) tool = Tool.Door;
            if (kb.digit5Key.wasPressedThisFrame) tool = Tool.Window;
            if (kb.digit6Key.wasPressedThisFrame) tool = Tool.Stair;
            if (kb.digit7Key.wasPressedThisFrame) tool = Tool.Roof;
            if (kb.digit8Key.wasPressedThisFrame) tool = Tool.Furniture;
            if (kb.rKey.wasPressedThisFrame) RotatePlacement();
            if (kb.rightBracketKey.wasPressedThisFrame || kb.pageUpKey.wasPressedThisFrame) StepLevel(1);
            if (kb.leftBracketKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame) StepLevel(-1);
            if (kb.f5Key.wasPressedThisFrame) SaveGame();
            if (kb.f9Key.wasPressedThisFrame) LoadGame();
            if (kb.tabKey.wasPressedThisFrame) ToggleOverviewMode();
        }

        if (cam == null || grid == null) return;

        // While walking, tools operate on the storey under the walker's feet —
        // otherwise the level filter rejects everything after using the stairs.
        bool walking = walk != null && walk.Walking;
        if (walking && walk.WalkerTransform != null)
            currentLevel = Mathf.Clamp(
                Mathf.FloorToInt((walk.WalkerTransform.position.y - grid.transform.position.y + 0.3f) / grid.wallHeight),
                0, grid.maxLevels - 1);

        // Level fly-in/fly-out: linear progress here, quadratic lift in ApplyLevelReveal.
        // Frozen while walking so upper storeys don't fly away over your head.
        float step = flyDuration > 0.001f ? Time.deltaTime / flyDuration : 1f;
        if (reveal != null && !walking)
        {
            for (int i = 0; i < reveal.Length; i++)
            {
                float target = TargetReveal(i);
                if (!Mathf.Approximately(reveal[i], target))
                {
                    reveal[i] = Mathf.MoveTowards(reveal[i], target, step);
                    grid.ApplyLevelReveal(i, reveal[i]);
                }
            }
        }
        // grid lines: hidden in overview, while walking, and at/above roof levels
        grid.SetGridLinesVisible(!overviewMode
            && !(walk != null && walk.Walking)
            && !grid.LevelHasRoofAtOrBelow(currentLevel));

        float gk = 1f - Mathf.Exp(-10f * Time.deltaTime);
        float glTarget = grid.LevelBaseY(currentLevel);
        if (Mathf.Abs(gridLineY - glTarget) > 0.0005f)
        {
            gridLineY = Mathf.Lerp(gridLineY, glTarget, gk);
            if (Mathf.Abs(gridLineY - glTarget) < 0.005f) gridLineY = glTarget;
            grid.SetGridLinesHeight(gridLineY);
        }

        // --- unified pointer: mouse in editor, single-finger touch on device.
        // A touch must also count on its release frame (isPressed is already false
        // then) — otherwise taps and drag-commits are invisible on touchscreens. ---
        bool pointerValid = false, press = false, pressedNow = false, releasedNow = false, cancelNow = false;
        Vector2 pointerPos = default;
        int uiPointerId = -1;

        // --- VR: the right-hand ray + trigger replaces mouse/touch entirely ---
        bool vrPointer = VRModeManager.Active;
        Ray vrRay = default;
        if (vrPointer)
        {
            pointerValid = VRModeManager.Instance.TryGetBuildPointer(
                out vrRay, out press, out pressedNow, out releasedNow, out cancelNow, out bool vrOverUI);
            if (pointerValid && vrOverUI)
            {
                // ray on the menu panel: never build through buttons
                if (!press) { floorDragging = false; gestureActive = false; gestureIsPan = false; }
                if (!hasPendingFurn) HideGhost();
                if (!hasPendingStair) HideStairGhost();
                HideRoofGhost();
                return;
            }
        }

        var ts = vrPointer ? null : Touchscreen.current;
        UnityEngine.InputSystem.Controls.TouchControl activeTouch = null;
        int activeTouches = 0;
        if (ts != null)
            foreach (var t in ts.touches)
                if (t.press.isPressed || t.press.wasReleasedThisFrame)
                {
                    activeTouches++;
                    if (activeTouch == null) activeTouch = t;
                }
        if (activeTouches >= 2)
        {
            // two fingers drive the camera
            floorDragging = false;
            gestureActive = false; gestureIsPan = false;
            if (!hasPendingFurn) HideGhost();
            if (!hasPendingStair) HideStairGhost();
            HideRoofGhost();
            return;
        }
        if (walking && activeTouch != null)
        {
            // Walk mode on touch: drags belong to the joystick/look zones, but a
            // quick tap on the look side becomes one build action (press + release
            // synthesized in the same frame, so every tool fires exactly once).
            Vector2 tpos = activeTouch.position.ReadValue();
            if (activeTouch.press.wasPressedThisFrame)
            {
                walkTapStart = tpos;
                walkTapTime = Time.unscaledTime;
                walkTapValid = tpos.x >= Screen.width * 0.4f;
            }
            if ((tpos - walkTapStart).magnitude > 30f) walkTapValid = false;
            if (activeTouch.press.wasReleasedThisFrame && walkTapValid && Time.unscaledTime - walkTapTime < 0.3f)
            {
                pointerValid = true;
                pointerPos = tpos;
                press = true;
                pressedNow = true;
                releasedNow = true;
                uiPointerId = activeTouch.touchId.ReadValue();
            }
            else
            {
                floorDragging = false;
                HideGhost(); HideStairGhost(); HideRoofGhost();
                return;
            }
        }
        else if (activeTouch != null)
        {
            pointerValid = true;
            pointerPos = activeTouch.position.ReadValue();
            press = activeTouch.press.isPressed;
            pressedNow = activeTouch.press.wasPressedThisFrame;
            releasedNow = activeTouch.press.wasReleasedThisFrame;
            uiPointerId = activeTouch.touchId.ReadValue();
        }
        else if (!vrPointer && Mouse.current != null)
        {
            var m = Mouse.current;
            pointerValid = true;
            pointerPos = m.position.ReadValue();
            press = m.leftButton.isPressed;
            pressedNow = m.leftButton.wasPressedThisFrame;
            releasedNow = m.leftButton.wasReleasedThisFrame;
            cancelNow = m.rightButton.wasPressedThisFrame;
        }
        if (!pointerValid)
        {
            gestureActive = false; gestureIsPan = false;
            if (!hasPendingFurn) HideGhost();
            if (!hasPendingStair) HideStairGhost();
            HideRoofGhost();
            return;
        }

        // pointer over UI: never build through buttons
        if (!vrPointer && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(uiPointerId))
        {
            if (!press) { floorDragging = false; gestureActive = false; gestureIsPan = false; }
            if (!hasPendingFurn) HideGhost();
            if (!hasPendingStair) HideStairGhost();
            HideRoofGhost();
            return;
        }

        // --- tap vs pan: a short press is a tap (build action on release); once the
        // pointer travels far enough it becomes a one-finger camera pan (except the
        // Floor tool, which keeps its rectangle drag) ---
        bool tapNow = false;
        if (pressedNow)
        {
            gestureActive = true;
            gestureIsPan = false;
            gestureStartPos = pointerPos;
            lastPointerPos = pointerPos;
        }
        if (gestureActive && press && !vrPointer) // VR pans with the stick, so a trigger press is always a tap
        {
            Vector2 frameDelta = pointerPos - lastPointerPos;
            lastPointerPos = pointerPos;
            if ((tool != Tool.Floor || overviewMode) && !gestureIsPan
                && Vector2.Distance(pointerPos, gestureStartPos) > Screen.height * 0.02f)
                gestureIsPan = true;
            if (gestureIsPan && camController != null && (walk == null || !walk.Walking))
                camController.PanByScreenDelta(frameDelta);
        }
        if (releasedNow && gestureActive)
        {
            tapNow = !gestureIsPan;
            gestureActive = false;
            gestureIsPan = false;
        }

        // View mode: free look — one-finger pan above still works, but every
        // build/erase interaction is suppressed
        if (overviewMode)
        {
            floorDragging = false;
            if (!hasPendingFurn) HideGhost();
            if (!hasPendingStair) HideStairGhost();
            HideRoofGhost();
            return;
        }

        if (tool != Tool.Floor || cancelNow) floorDragging = false;
        if (tool != Tool.Erase) eraseLock = EraseKind.None;
        if (tool != Tool.Furniture) hasPendingFurn = false;
        if (tool != Tool.Stair) { hasPendingStair = false; HideStairGhost(); }
        if (tool != Tool.Roof) HideRoofGhost();

        float planeY = grid.transform.position.y + grid.LevelBaseY(currentLevel);
        var plane = new Plane(Vector3.up, new Vector3(0f, planeY, 0f));
        var ray = vrPointer ? vrRay : cam.ScreenPointToRay(pointerPos);

        // Geometry-first picking: aim at what you see (walls, furniture, stairs,
        // roofs are tall — the flat build plane alone would force aiming at their
        // base, unusable in first-person). Fall back to the plane over empty ground.
        Vector3 p = default;
        bool gotPoint = false;
        float bestDist = float.MaxValue;
        float lvBase = grid.LevelBaseY(currentLevel);
        foreach (var h in Physics.RaycastAll(ray, 200f))
        {
            if (h.collider is CharacterController) continue; // the walker itself
            float ly = h.point.y - grid.transform.position.y;
            if (ly < lvBase - 0.1f || ly > lvBase + grid.wallHeight + 0.2f) continue; // other levels
            if (h.distance < bestDist)
            {
                bestDist = h.distance;
                p = h.point;
                gotPoint = true;
            }
        }
        if (!gotPoint)
        {
            if (!plane.Raycast(ray, out float enter))
            {
                if (!press) floorDragging = false;
                HideGhost();
                return;
            }
            p = ray.GetPoint(enter);
        }

        switch (tool)
        {
            case Tool.Wall:
            {
                if (grid.TryGetEdgeAt(p, out int x, out int z, out int dir, out _))
                {
                    // Tap-only. Like the floor tool: with a style selected, tapping
                    // an existing wall repaints it instead of being blocked.
                    bool canPlace = grid.CanPlaceWall(x, z, dir, currentLevel);
                    bool canPaint = !string.IsNullOrEmpty(selectedWallStyle) && grid.HasWall(x, z, dir, currentLevel);
                    bool ok = canPlace || canPaint;
                    ShowGhost(grid.WallCenterWorld(x, z, dir, currentLevel), grid.WallScaleFor(dir) * 1.02f, ok);
                    if (tapNow)
                    {
                        if (canPlace) grid.PlaceWall(x, z, dir, currentLevel, WallOpening.None, selectedWallStyle);
                        else if (canPaint) grid.SetWallStyle(x, z, dir, currentLevel, selectedWallStyle);
                    }
                }
                else HideGhost();
                break;
            }
            case Tool.Floor:
            {
                grid.CellAtUnclamped(p, out int rawX, out int rawZ);
                int cx = Mathf.Clamp(rawX, 0, grid.width - 1);
                int cz = Mathf.Clamp(rawZ, 0, grid.height - 1);
                bool inPlot = rawX == cx && rawZ == cz;

                if (!floorDragging && pressedNow && inPlot)
                {
                    floorDragging = true;
                    dragStart = new Vector2Int(cx, cz);
                }

                if (floorDragging)
                {
                    // Rectangle drag: anchor at press, live N x M preview, commit on release.
                    int x0 = Mathf.Min(dragStart.x, cx), x1 = Mathf.Max(dragStart.x, cx);
                    int z0 = Mathf.Min(dragStart.y, cz), z1 = Mathf.Max(dragStart.y, cz);
                    dragRect = new RectInt(x0, z0, x1 - x0 + 1, z1 - z0 + 1);
                    Vector3 center = (grid.FloorCenterWorld(x0, z0, currentLevel) + grid.FloorCenterWorld(x1, z1, currentLevel)) * 0.5f;
                    Vector3 scale = new Vector3(dragRect.width * grid.cellSize, 0.12f, dragRect.height * grid.cellSize);
                    ShowGhost(center, scale * 1.01f, true);

                    if (releasedNow)
                    {
                        // Lay new tiles; with a style selected, also repaint existing ones
                        for (int x = x0; x <= x1; x++)
                            for (int z = z0; z <= z1; z++)
                            {
                                if (!grid.HasFloor(x, z, currentLevel))
                                    grid.PlaceFloor(x, z, currentLevel, selectedFloorStyle);
                                else if (!string.IsNullOrEmpty(selectedFloorStyle))
                                    grid.SetFloorStyle(x, z, currentLevel, selectedFloorStyle);
                            }
                        floorDragging = false;
                    }
                }
                else if (inPlot)
                {
                    bool ok = grid.CanPlaceFloor(cx, cz, currentLevel)
                        || (!string.IsNullOrEmpty(selectedFloorStyle) && grid.HasFloor(cx, cz, currentLevel));
                    ShowGhost(grid.FloorCenterWorld(cx, cz, currentLevel), grid.FloorScale() * 1.02f, ok);
                }
                else HideGhost();
                break;
            }
            case Tool.Door:
            case Tool.Window:
            {
                // Click semantics (no drag paint): empty edge -> wall with opening,
                // solid wall -> add opening, same opening -> revert to solid wall.
                if (grid.TryGetEdgeAt(p, out int x, out int z, out int dir, out _))
                {
                    var opening = tool == Tool.Door ? WallOpening.Door : WallOpening.Window;
                    bool valid = grid.HasWall(x, z, dir, currentLevel) || grid.CanPlaceWall(x, z, dir, currentLevel);
                    ShowGhost(grid.WallCenterWorld(x, z, dir, currentLevel), grid.WallScaleFor(dir) * 1.03f, valid);
                    if (valid && tapNow)
                        grid.ToggleOpening(x, z, dir, opening, currentLevel,
                            tool == Tool.Door ? selectedDoorStyle : selectedWindowStyle);
                }
                else HideGhost();
                break;
            }
            case Tool.Stair:
            {
                // Confirm flow: a tap parks the stair ghost; Confirm builds it.
                HideGhost();
                if (tapNow && grid.TryGetCellAt(p, out int px, out int pz))
                {
                    pendingStairCell = new Vector2Int(px, pz);
                    hasPendingStair = true;
                }
                if (hasPendingStair) RefreshPendingGhost();
                else if (grid.TryGetCellAt(p, out int x, out int z))
                {
                    bool ok = grid.CanPlaceStair(x, z, stairRot, currentLevel);
                    ShowStairGhost(grid.StairCenterWorld(x, z, stairRot, currentLevel), GridSystem.StairYaw(stairRot), ok);
                }
                else HideStairGhost();
                break;
            }
            case Tool.Roof:
            {
                // One-click auto roof: the footprint is detected from the storey-below
                // walls; the ghost is a flat green plane over it.
                HideGhost();
                var cells = grid.ComputeRoofableCells(currentLevel);
                if (cells.Count == 0) { HideRoofGhost(); break; }

                int hash = 17;
                foreach (var fc in cells) hash = hash * 31 + fc.x * 53 + fc.y;
                if (roofGhost == null || hash != roofFootprintHash || currentLevel != roofFootprintLevel)
                {
                    roofFootprintHash = hash;
                    roofFootprintLevel = currentLevel;
                    if (roofGhost == null) roofGhost = new GameObject("RoofGhost").transform;
                    for (int i = roofGhost.childCount - 1; i >= 0; i--) Destroy(roofGhost.GetChild(i).gameObject);
                    grid.BuildFootprintPlane(roofGhost, cells, ghostOkMat);
                }
                // the roofable-cell preview only ever shows valid area, so it's always green
                foreach (var r in roofGhost.GetComponentsInChildren<MeshRenderer>())
                    r.sharedMaterial = ghostOkMat;
                roofGhost.gameObject.SetActive(true);
                roofGhost.position = grid.transform.position + new Vector3(0f, grid.LevelBaseY(currentLevel) + 0.05f, 0f);

                if (tapNow)
                    grid.PlaceAutoRoof(currentLevel);
                break;
            }
            case Tool.Furniture:
            {
                // Confirm flow: a tap parks the ghost; Confirm places it, Cancel exits.
                if (selectedFurniture == null) { HideGhost(); break; }
                if (tapNow)
                {
                    var fpt = GridSystem.FurnFootprint(selectedFurniture, furnRot);
                    Vector3 lp = grid.transform.InverseTransformPoint(p);
                    float s = grid.SubCell;
                    int sx = Mathf.RoundToInt((lp.x - fpt.x * 0.5f) / s);
                    int sz = Mathf.RoundToInt((lp.z - fpt.y * 0.5f) / s);
                    grid.SnapFurnitureToWall(selectedFurniture, ref sx, ref sz, furnRot, currentLevel);
                    pendingSx = sx; pendingSz = sz;
                    hasPendingFurn = true;
                }
                if (hasPendingFurn) RefreshPendingGhost();
                else
                {
                    // hover preview before the first tap (desktop nicety)
                    var fpt = GridSystem.FurnFootprint(selectedFurniture, furnRot);
                    Vector3 lp = grid.transform.InverseTransformPoint(p);
                    float s = grid.SubCell;
                    int sx = Mathf.RoundToInt((lp.x - fpt.x * 0.5f) / s);
                    int sz = Mathf.RoundToInt((lp.z - fpt.y * 0.5f) / s);
                    grid.SnapFurnitureToWall(selectedFurniture, ref sx, ref sz, furnRot, currentLevel);
                    bool ok = grid.CanPlaceFurniture(selectedFurniture, sx, sz, furnRot, currentLevel);
                    Vector3 center = grid.transform.TransformPoint(new Vector3(
                        sx * s + fpt.x * 0.5f,
                        grid.LevelBaseY(currentLevel) + 0.12f + selectedFurniture.size.y * 0.5f,
                        sz * s + fpt.y * 0.5f));
                    ShowGhost(center, new Vector3(fpt.x, selectedFurniture.size.y, fpt.y) * 1.02f, ok);
                }
                break;
            }
            case Tool.Erase:
            {
                if (pressedNow) eraseLock = EraseKind.None; // fresh gesture, fresh lock

                bool onEdge = grid.TryGetEdgeAt(p, out int ex, out int ez, out int edir, out float edist);
                if (Allow(EraseKind.Wall) && onEdge && edist < grid.cellSize * 0.3f && grid.HasWall(ex, ez, edir, currentLevel))
                {
                    ShowGhost(grid.WallCenterWorld(ex, ez, edir, currentLevel), grid.WallScaleFor(edir) * 1.05f, false);
                    if (tapNow) { grid.RemoveWall(ex, ez, edir, currentLevel); eraseLock = EraseKind.Wall; }
                }
                else if (Allow(EraseKind.Stair) && grid.TryGetCellAt(p, out int sx, out int sz) && grid.TryGetStair(sx, sz, currentLevel, out var sp))
                {
                    bool alongX = (sp.rot & 1) == 0;
                    Vector3 c = grid.StairCenterWorld(sp.x, sp.z, sp.rot, sp.level);
                    Vector3 sc = alongX
                        ? new Vector3(2f * grid.cellSize, 1.3f, grid.cellSize)
                        : new Vector3(grid.cellSize, 1.3f, 2f * grid.cellSize);
                    ShowGhost(c + Vector3.up * 0.65f, sc * 1.03f, false);
                    if (tapNow) { grid.RemoveStairAt(sx, sz, currentLevel); eraseLock = EraseKind.Stair; }
                }
                else if (Allow(EraseKind.Furniture) && grid.TryGetFurnitureAt(p, currentLevel, out var fu))
                {
                    var fit = grid.FindItem(fu.itemName);
                    if (fit != null)
                    {
                        var fd2 = GridSystem.FurnFootprint(fit, fu.rot);
                        float ss = grid.SubCell;
                        Vector3 fc = grid.transform.TransformPoint(new Vector3(
                            fu.sx * ss + fd2.x * 0.5f,
                            grid.LevelBaseY(currentLevel) + 0.12f + fit.size.y * 0.5f,
                            fu.sz * ss + fd2.y * 0.5f));
                        ShowGhost(fc, new Vector3(fd2.x, fit.size.y, fd2.y) * 1.05f, false);
                    }
                    if (tapNow) { grid.RemoveFurniture(fu); eraseLock = EraseKind.Furniture; }
                }
                else if (Allow(EraseKind.Roof) && grid.TryGetCellAt(p, out int rx, out int rz) && grid.HasRoofAt(rx, rz, currentLevel))
                {
                    ShowGhost(grid.FloorCenterWorld(rx, rz, currentLevel) + Vector3.up * 0.5f,
                        new Vector3(grid.cellSize, 1.2f, grid.cellSize) * 1.03f, false);
                    if (tapNow) { grid.RemoveRoofAt(rx, rz, currentLevel); eraseLock = EraseKind.Roof; }
                }
                else if (Allow(EraseKind.Floor) && grid.TryGetCellAt(p, out int cx, out int cz) && grid.HasFloor(cx, cz, currentLevel))
                {
                    ShowGhost(grid.FloorCenterWorld(cx, cz, currentLevel), grid.FloorScale() * 1.05f, false);
                    if (tapNow) { grid.RemoveFloor(cx, cz, currentLevel); eraseLock = EraseKind.Floor; }
                }
                else HideGhost();
                break;
            }
        }
    }

    void ShowGhost(Vector3 worldPos, Vector3 scale, bool ok)
    {
        ghost.gameObject.SetActive(true);
        ghost.position = worldPos;
        ghost.localScale = scale;
        ghostRenderer.sharedMaterial = ok ? ghostOkMat : ghostBadMat;
    }

    void HideGhost()
    {
        if (ghost != null) ghost.gameObject.SetActive(false);
    }

    void ShowStairGhost(Vector3 worldPos, float yaw, bool ok)
    {
        if (stairGhost == null) return;
        stairGhost.gameObject.SetActive(true);
        stairGhost.position = worldPos;
        stairGhost.rotation = Quaternion.Euler(0f, yaw, 0f);
        var m = ok ? ghostOkMat : ghostBadMat;
        foreach (var r in stairGhostRenderers) r.sharedMaterial = m;
    }

    void HideStairGhost()
    {
        if (stairGhost != null) stairGhost.gameObject.SetActive(false);
    }

    void HideRoofGhost()
    {
        if (roofGhost != null) roofGhost.gameObject.SetActive(false);
    }

    static Material CreateGhostMaterial(Color c)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetFloat("_Surface", 1f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", c);
        return m;
    }

}
