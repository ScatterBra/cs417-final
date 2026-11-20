using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime-built uGUI layer: bottom tool bar, level switcher, view controls,
/// save/load and toast. Text is TextMeshPro (SDF, crisp at any scale) with two
/// faces: JetBrains Mono for the title and primary chrome (tool bar, top bars,
/// level switcher, system labels) and EB Garamond for secondary content (mode
/// buttons, pop-up panels, brief/verdict prose). Pre-baked "* SDF" assets in
/// Resources/Fonts carry the charset, with dynamic runtime fill as fallback.
/// Two canvas layers: the root canvas holds panels that rarely change; a
/// nested DynamicCanvas holds the toast/budget/drag labels so their frequent
/// updates only rebuild that tiny canvas. CanvasScaler keeps buttons a usable
/// size on any phone resolution. Buttons call the same public methods as the
/// editor keyboard shortcuts.
/// </summary>
public partial class GameUI : MonoBehaviour
{
    public static GameUI Instance { get; private set; }

    static readonly Color PanelColor = new Color(0.10f, 0.12f, 0.14f, 0.72f);
    static readonly Color ButtonColor = new Color(0.16f, 0.19f, 0.22f, 0.92f);
    static readonly Color AccentColor = new Color(0.95f, 0.55f, 0.25f, 0.95f);

    BuildManager bm;
    IsoCameraController isoCam;
    TMP_FontAsset fontMono;  // JetBrains Mono: title + primary chrome
    TMP_FontAsset fontSerif; // EB Garamond: secondary panels + prose
    TMP_Text toastText;
    float toastUntil;
    TMP_Text levelLabel;
    TMP_Text dragLabel;
    Image overviewBtnBg;
    RectTransform catalogPanel;
    RectTransform floorStylePanel;
    RectTransform wallStylePanel;
    RectTransform doorStylePanel;
    RectTransform windowStylePanel;
    WalkController walkCtrl;
    MissionManager mission;
    TMP_Text budgetText;
    RectTransform lvPanelRt;
    RectTransform viewPanelRt;
    RectTransform placePanelRt;
    RectTransform menuPanel;
    readonly List<GameObject> isoOnlyButtons = new List<GameObject>(); // RotL/RotR/Top: hidden in walk
    readonly List<GameObject> storyOnlyButtons = new List<GameObject>(); // Brief/Submit: hidden in free mode
    RectTransform briefPanel;
    TMP_Text briefText;
    RectTransform resultPanel;
    TMP_Text resultText;
    // normal = the un-selected tint: dark box for text tools, white for bare icons
    readonly List<(BuildManager.Tool tool, Image bg, Color normal)> toolButtons = new List<(BuildManager.Tool, Image, Color)>();
    readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();
    Canvas rootCanvas;
    RawImage menuBackdrop;     // frosted glass: blurred capture of the scene
    RenderTexture menuBlurRT;
    Sprite pillFill, pillOutline; // runtime-generated capsule sprites

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        bm = FindFirstObjectByType<BuildManager>();
        isoCam = FindFirstObjectByType<IsoCameraController>();
        walkCtrl = FindFirstObjectByType<WalkController>();
        mission = FindFirstObjectByType<MissionManager>();
        fontMono = LoadFontAsset("JetBrainsMono SDF", "JetBrainsMono");
        fontSerif = LoadFontAsset("EBGaramond SDF", "EBGaramond");

        if (EventSystem.current == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var canvasGo = new GameObject("GameCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        // Screen Space - Overlay: always on top, no camera/render-pipeline dependency
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;
        rootCanvas = canvas;
        var root = canvas.transform;

        // ---- bottom tool bar ----
        var tools = new (BuildManager.Tool tool, string label)[]
        {
            (BuildManager.Tool.Wall, "Wall"),
            (BuildManager.Tool.Floor, "Floor"),
            (BuildManager.Tool.Door, "Door"),
            (BuildManager.Tool.Window, "Window"),
            (BuildManager.Tool.Stair, "Stair"),
            (BuildManager.Tool.Roof, "Roof"),
            (BuildManager.Tool.Furniture, "Furn"),
            (BuildManager.Tool.Erase, "Erase"),
        };
        //底部工具栏bh：按钮高度，bw：按钮宽度，gap：按钮间距
        const float bw = 88f, bh = 80f, gap = 10f;
        float total = tools.Length * bw + (tools.Length - 1) * gap;
        var bar = MakePanel(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 25f), new Vector2(total + 24f, bh + 20f));
        for (int i = 0; i < tools.Length; i++)
        {
            var entry = tools[i];
            float x = -total * 0.5f + bw * 0.5f + i * (bw + gap);
            UnityEngine.Events.UnityAction onClick = () =>
            {
                bm.SetTool(entry.tool);
                if (entry.tool == BuildManager.Tool.Furniture) ToggleCatalog();
                else SetCatalogVisible(false);
                if (entry.tool == BuildManager.Tool.Floor) ToggleFloorStyles();
                else SetFloorStylesVisible(false);
                // per-kind style panels: wall / door / window each has its own catalog
                if (entry.tool == BuildManager.Tool.Wall) TogglePanel(wallStylePanel); else HidePanel(wallStylePanel);
                if (entry.tool == BuildManager.Tool.Door) TogglePanel(doorStylePanel); else HidePanel(doorStylePanel);
                if (entry.tool == BuildManager.Tool.Window) TogglePanel(windowStylePanel); else HidePanel(windowStylePanel);
            };
            // Erase has shipped icon art; the rest stay text boxes until theirs exists
            bool hasIcon = entry.tool == BuildManager.Tool.Erase;
            var btn = hasIcon
                ? MakeIconButton(bar, "Erase", new Vector2(x, 0f), new Vector2(52f, 52f), onClick)
                : MakeButton(bar, entry.label, new Vector2(x, 0f), new Vector2(bw, bh), onClick);
            toolButtons.Add((entry.tool, btn.GetComponent<Image>(), hasIcon ? Color.white : ButtonColor));
        }
        BuildCatalogPanel(root, bh);
        BuildFloorStylePanel(root, bh);
        wallStylePanel = BuildKindStylePanel(root, bh, WallOpening.None);
        doorStylePanel = BuildKindStylePanel(root, bh, WallOpening.Door);
        windowStylePanel = BuildKindStylePanel(root, bh, WallOpening.Window);

        // ---- level switcher, right edge: bare icons, no panel box ----
        var lvPanel = MakeBar(root, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(104f, 240f));
        MakeIconButton(lvPanel, "Up_1", new Vector2(0f, 78f), new Vector2(56f, 56f), () => bm.StepLevel(1));
        levelLabel = MakeLabel(lvPanel, "1/4", 30, new Vector2(0f, 18f), new Vector2(84f, 40f));
        var downBtn = MakeIconButton(lvPanel, "Up_1", new Vector2(0f, -42f), new Vector2(56f, 56f), () => bm.StepLevel(-1));
        downBtn.transform.localEulerAngles = new Vector3(0f, 0f, 180f); // Up flipped = Down
        lvPanelRt = lvPanel;

        // ---- placement confirm flow: rotate / confirm / cancel ----
        placePanelRt = MakeBar(root, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 130f), new Vector2(150f, 200f));
        MakeIconButton(placePanelRt, "Rotate", new Vector2(0f, 64f), new Vector2(56f, 56f), () => bm.RotatePlacement());
        var confirmBtn = MakeIconButton(placePanelRt, "OK", new Vector2(0f, 0f), new Vector2(56f, 56f), () => bm.ConfirmPlacement());
        confirmBtn.GetComponent<Image>().color = new Color(0.55f, 0.95f, 0.6f, 1f); // green-tinted confirm
        MakeIconButton(placePanelRt, "Canel", new Vector2(0f, -64f), new Vector2(56f, 56f), () => bm.CancelPlacement());
        placePanelRt.gameObject.SetActive(false);


        //最后一个参数是view control面板高度
        //第二个Vector()：第一个负数是x轴偏移量，第二个负数是y轴偏移量
        var viewPanel = MakeBar(root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -70f), new Vector2(7f * 78f + 24f, 76f));
        string[] vLabels = { "RotL", "RotR", "Top", "View", "Walk", "+", "-" };
        string[] vIcons = { "RotL", "RotR", "Top", "View", "Walk_2", "+", "-" };
        for (int i = 0; i < vLabels.Length; i++)
        {
            float x = -(7f * 78f) * 0.5f + 39f + i * 78f;
            string label = vLabels[i];
            var btn = MakeIconButton(viewPanel, vIcons[i], new Vector2(x, 0f), new Vector2(48f, 48f), () =>
            {
                switch (label)
                {
                    case "RotL": isoCam.RotateStep(-1); break;
                    case "RotR": isoCam.RotateStep(1); break;
                    case "Top": isoCam.ToggleBirdView(); break;
                    case "View": bm.ToggleOverviewMode(); break;
                    case "Walk": if (walkCtrl != null) walkCtrl.Toggle(); break;
                    case "+": isoCam.ZoomBy(0.8f); break;
                    case "-": isoCam.ZoomBy(1.25f); break;
                }
            });
            if (label == "View") overviewBtnBg = btn.GetComponent<Image>();
            if (label == "RotL" || label == "RotR" || label == "Top") isoOnlyButtons.Add(btn.gameObject);
        }
        viewPanelRt = viewPanel;

        //最后一个参数是save/load面板高度
        //第二个Vector()：第一个负数是x轴偏移量，第二个负数是y轴偏移量
        var filePanel = MakeBar(root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -70f), new Vector2(536f, 76f));
        MakeIconButton(filePanel, "Save", new Vector2(-212f, 0f), new Vector2(48f, 48f), () => bm.SaveGame());
        // Load / Submit have no icon art yet: boxless text buttons keep the row uniform
        var loadBtn = MakeButton(filePanel, "Load", new Vector2(-106f, 0f), new Vector2(92f, 56f), () => bm.LoadGame());
        loadBtn.GetComponent<Image>().color = Color.clear; // invisible, still the raycast target
        var briefBtn = MakeIconButton(filePanel, "Brief", new Vector2(0f, 0f), new Vector2(48f, 48f), ToggleBriefPanel);
        var submitBtn = MakeButton(filePanel, "Submit", new Vector2(106f, 0f), new Vector2(92f, 56f), SubmitMission);
        submitBtn.GetComponent<Image>().color = Color.clear;
        storyOnlyButtons.Add(briefBtn.gameObject);
        storyOnlyButtons.Add(submitBtn.gameObject);
        MakeIconButton(filePanel, "Menu", new Vector2(212f, 0f), new Vector2(48f, 48f), ReturnToMenu);

        BuildMissionPanels(root);
        BuildMainMenu(root); // draws on top of every static panel

        // ---- dynamic layer: nested canvas for the high-frequency labels
        // (toast fade, drag size, budget). A nested Canvas isolates mesh
        // rebuilds, so these updates never re-tessellate the whole UI. No
        // raycaster: display-only, never blocks clicks. Built after the menu,
        // so it draws above it — Update blanks the budget while the menu is up.
        var dynGo = new GameObject("DynamicCanvas", typeof(RectTransform), typeof(Canvas));
        dynGo.transform.SetParent(root, false);
        var dynRt = (RectTransform)dynGo.transform;
        dynRt.anchorMin = Vector2.zero;
        dynRt.anchorMax = Vector2.one;
        dynRt.offsetMin = Vector2.zero;
        dynRt.offsetMax = Vector2.zero;
        var dyn = dynGo.transform;

        // ---- budget display, top-center ----
        budgetText = MakeLabel(dyn, "", 26, Vector2.zero, new Vector2(500f, 32f));
        budgetText.raycastTarget = false;
        var brt = (RectTransform)budgetText.transform;
        brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 1f);
        brt.anchoredPosition = new Vector2(0f, -56f);

        // ---- toast + drag-size label ----
        toastText = MakeLabel(dyn, "", 28, Vector2.zero, new Vector2(600f, 40f));
        toastText.raycastTarget = false;
        var toastRt = (RectTransform)toastText.transform;
        toastRt.anchorMin = toastRt.anchorMax = toastRt.pivot = new Vector2(0.5f, 1f);
        toastRt.anchoredPosition = new Vector2(0f, -20f);

        dragLabel = MakeLabel(dyn, "", 30, Vector2.zero, new Vector2(240f, 40f));
        dragLabel.raycastTarget = false;
        var dragRt = (RectTransform)dragLabel.transform;
        dragRt.anchorMin = dragRt.anchorMax = dragRt.pivot = new Vector2(0.5f, 0f);
        dragRt.anchoredPosition = new Vector2(0f, bh + 48f);
    }

    void Update()
    {
        if (bm == null) return;
        if (bm.tool != BuildManager.Tool.Furniture) SetCatalogVisible(false);
        foreach (var (tool, bg, normal) in toolButtons)
            bg.color = bm.tool == tool ? AccentColor : normal;
        if (overviewBtnBg != null)
            overviewBtnBg.color = bm.OverviewOn ? AccentColor : Color.white;
        if (levelLabel != null && bm.grid != null)
            levelLabel.text = (bm.currentLevel + 1) + "/" + bm.grid.maxLevels;
        if (toastText != null && Time.unscaledTime > toastUntil)
            toastText.text = "";
        if (dragLabel != null)
            dragLabel.text = bm.DraggingRect ? bm.DragRect.width + " x " + bm.DragRect.height : "";
        if (budgetText != null)
        {
            // the dynamic canvas draws above the main menu, so blank it there
            bool menuOpen = menuPanel != null && menuPanel.gameObject.activeSelf;
            if (!menuOpen && mission != null && mission.StoryActive && mission.Current != null && bm.grid != null)
            {
                int cost = bm.grid.ComputeTotalCost();
                budgetText.text = "$ " + cost.ToString("N0") + " / " + mission.Current.budget.ToString("N0");
                budgetText.color = cost > mission.Current.budget ? new Color(1f, 0.4f, 0.35f) : Color.white;
            }
            else budgetText.text = "";
        }

        // free mode hides mission buttons
        bool story = mission != null && mission.StoryActive;
        foreach (var go in storyOnlyButtons)
            if (go.activeSelf != story) go.SetActive(story);

        // walk mode: level panel + iso-only view buttons hidden;
        // View / Walk(=exit) / + / - stay available
        bool walking = walkCtrl != null && walkCtrl.Walking;
        if (lvPanelRt != null && lvPanelRt.gameObject.activeSelf == walking)
            lvPanelRt.gameObject.SetActive(!walking);
        foreach (var go in isoOnlyButtons)
            if (go.activeSelf == walking) go.SetActive(!walking);
        bool showPlace = bm.ShowPlacementControls;
        if (placePanelRt != null && placePanelRt.gameObject.activeSelf != showPlace)
            placePanelRt.gameObject.SetActive(showPlace);
    }

    public void ShowToast(string msg)
    {
        if (toastText == null) return;
        toastText.text = msg;
        toastUntil = Time.unscaledTime + 2f;
    }

    static TMP_FontAsset LoadFontAsset(string sdfName, string sourceName)
    {
        // Editor-baked SDF asset first: charset pre-generated, so no runtime
        // glyph rasterization stutter and crisp text at any scale.
        var baked = Resources.Load<TMP_FontAsset>("Fonts/" + sdfName);
        if (baked != null) return baked;

        // Fallback: build a dynamic SDF asset from the bundled TTF — glyphs get
        // added on demand (slower first display, but nothing is ever missing).
        var bundled = Resources.Load<Font>("Fonts/" + sourceName);
        if (bundled != null) return TMP_FontAsset.CreateFontAsset(bundled);
        return TMP_Settings.defaultFontAsset;
    }

    void BuildMainMenu(Transform root)
    {
        // the panel Image itself is invisible but still swallows clicks;
        // the look comes from the blurred-scene backdrop child below
        var go = new GameObject("MainMenu", typeof(Image));
        go.transform.SetParent(root, false);
        go.GetComponent<Image>().color = Color.clear;
        menuPanel = (RectTransform)go.transform;
        menuPanel.anchorMin = Vector2.zero;
        menuPanel.anchorMax = Vector2.one;
        menuPanel.offsetMin = Vector2.zero;
        menuPanel.offsetMax = Vector2.zero;

        var bgGo = new GameObject("Backdrop", typeof(RawImage));
        bgGo.transform.SetParent(menuPanel, false);
        menuBackdrop = bgGo.GetComponent<RawImage>();
        menuBackdrop.color = new Color(0.60f, 0.62f, 0.66f, 1f); // darken so white text reads
        menuBackdrop.raycastTarget = false;
        var bgRt = (RectTransform)bgGo.transform;
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;

        // title in the mono face; 60pt so the long English title stays on screen
        var title = MakeLabel(menuPanel, "Concrete Heart", 60, new Vector2(-400f, 150f), new Vector2(800f, 90f));
        title.fontStyle = FontStyles.Bold;

        MakePillButton(menuPanel, "STORY MODE", new Vector2(-500f, -20f), new Vector2(340f, 84f), () => StartSession(false));
        MakePillButton(menuPanel, "FREE BUILD", new Vector2(-500f, -150f), new Vector2(340f, 84f), () => StartSession(true));

        StartCoroutine(CaptureMenuBackdrop());
    }

    /// <summary>Frosted-glass capsule: milky fill + thin white outline over the
    /// blurred backdrop, wide-tracked mono caps — matches the reference art.</summary>
    Button MakePillButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var bg = go.GetComponent<Image>();
        bg.sprite = PillSprite(false);
        bg.type = Image.Type.Sliced;
        bg.color = new Color(1f, 1f, 1f, 0.16f);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        var oGo = new GameObject("Outline", typeof(Image));
        oGo.transform.SetParent(go.transform, false);
        var ol = oGo.GetComponent<Image>();
        ol.sprite = PillSprite(true);
        ol.type = Image.Type.Sliced;
        ol.color = new Color(1f, 1f, 1f, 0.8f);
        ol.raycastTarget = false;
        var ort = (RectTransform)oGo.transform;
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = Vector2.zero;
        ort.offsetMax = Vector2.zero;

        var txt = MakeLabel(go.transform, label, 26, Vector2.zero, size);
        txt.raycastTarget = false;
        txt.characterSpacing = 12f;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        return go.GetComponent<Button>();
    }

    /// <summary>Runtime-built 9-sliced capsule sprite (fill or ~2px outline):
    /// an SDF rounded-rect rasterized once into an 88px texture, so any button
    /// size stays crisp and no art asset is needed.</summary>
    Sprite PillSprite(bool outline)
    {
        var cached = outline ? pillOutline : pillFill;
        if (cached != null) return cached;
        const int n = 88;
        const float half = 44f, box = 2f, radius = 40f; // 2px pad; slice border = 42
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - box, 0f);
                float qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - box, 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy) - radius; // SDF rounded rect
                float a = outline
                    ? Mathf.Clamp01(1.6f - Mathf.Abs(d))  // ~2px ring on the edge
                    : Mathf.Clamp01(0.5f - d);            // soft-edged solid
                px[y * n + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.Apply(false, true);
        tex.wrapMode = TextureWrapMode.Clamp;
        var sp = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0,
                               SpriteMeshType.FullRect, new Vector4(42f, 42f, 42f, 42f));
        if (outline) pillOutline = sp; else pillFill = sp;
        return sp;
    }

    /// <summary>Grab one clean frame of the 3D scene (UI hidden), cheap-blur it
    /// by bilinear ping-pong downsampling, and use it as the menu backdrop.
    /// No custom shader involved, so it works under URP + Screen Space Overlay.</summary>
    System.Collections.IEnumerator CaptureMenuBackdrop()
    {
        rootCanvas.enabled = false; // this frame renders scene-only
        yield return new WaitForEndOfFrame();
        int w = Mathf.Max(Screen.width, 16), h = Mathf.Max(Screen.height, 16);
        var full = RenderTexture.GetTemporary(w, h, 0);
        ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
        if (menuBlurRT == null || menuBlurRT.width != w / 4 || menuBlurRT.height != h / 4)
        {
            if (menuBlurRT != null) menuBlurRT.Release();
            menuBlurRT = new RenderTexture(w / 4, h / 4, 0);
        }
        // strict 2x-per-step cascade: every bilinear tap covers all source
        // pixels, so the result is smooth instead of blocky-aliased
        var half2 = RenderTexture.GetTemporary(w / 2, h / 2, 0);
        var eighth = RenderTexture.GetTemporary(w / 8, h / 8, 0);
        var sixteenth = RenderTexture.GetTemporary(w / 16, h / 16, 0);
        Graphics.Blit(full, half2);
        Graphics.Blit(half2, menuBlurRT);      // 1/4
        Graphics.Blit(menuBlurRT, eighth);     // 1/8
        Graphics.Blit(eighth, sixteenth);      // 1/16
        Graphics.Blit(sixteenth, eighth);      // back up, smoothing as it goes
        Graphics.Blit(eighth, menuBlurRT);     // final 1/4
        RenderTexture.ReleaseTemporary(sixteenth);
        RenderTexture.ReleaseTemporary(eighth);
        RenderTexture.ReleaseTemporary(half2);
        RenderTexture.ReleaseTemporary(full);
        menuBackdrop.texture = menuBlurRT;
        // capture APIs hand back a bottom-up image on some graphics backends
        menuBackdrop.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
        rootCanvas.enabled = true;
    }

    void StartSession(bool freeMode)
    {
        menuPanel.gameObject.SetActive(false);
        if (bm != null) bm.BeginSession(freeMode);
        if (mission != null) mission.BeginSession(!freeMode);
        ShowToast(freeMode ? "Free build: no budget, build as you like" : "Story mode: tap Brief to see the client's needs");
    }

    void ReturnToMenu()
    {
        if (walkCtrl != null && walkCtrl.Walking) walkCtrl.Toggle();
        if (bm != null) bm.EndSession();
        if (briefPanel != null) briefPanel.gameObject.SetActive(false);
        if (resultPanel != null) resultPanel.gameObject.SetActive(false);
        SetCatalogVisible(false);
        SetFloorStylesVisible(false);
        HidePanel(wallStylePanel);
        HidePanel(doorStylePanel);
        HidePanel(windowStylePanel);
        menuPanel.gameObject.SetActive(true);
        StartCoroutine(CaptureMenuBackdrop()); // re-blur: the scene has changed
    }

    void BuildMissionPanels(Transform root)
    {
        briefPanel = MakePanel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(540f, 420f));
        briefText = MakeScrollText(briefPanel, new Vector2(0f, 24f), new Vector2(490f, 330f), 25f, fontSerif);
        MakeButton(briefPanel, "Close", new Vector2(0f, -176f), new Vector2(130f, 48f), () => briefPanel.gameObject.SetActive(false), fontSerif);
        briefPanel.gameObject.SetActive(false);

        resultPanel = MakePanel(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(580f, 480f));
        resultText = MakeScrollText(resultPanel, new Vector2(0f, 26f), new Vector2(530f, 380f), 23f, fontSerif);
        MakeButton(resultPanel, "Next Job", new Vector2(-74f, -206f), new Vector2(132f, 48f), () =>
        {
            mission.NewBrief();
            resultPanel.gameObject.SetActive(false);
            ToggleBriefPanel();
        }, fontSerif);
        MakeButton(resultPanel, "Close", new Vector2(74f, -206f), new Vector2(132f, 48f), () => resultPanel.gameObject.SetActive(false), fontSerif);
        resultPanel.gameObject.SetActive(false);
    }

    /// <summary>Masked, vertically scrollable text area: long briefs/verdicts
    /// scroll instead of overflowing the panel. The returned TMP text is the
    /// scroll content; its height follows the text via ContentSizeFitter.</summary>
    TMP_Text MakeScrollText(RectTransform panel, Vector2 anchoredPos, Vector2 size, float fontSize, TMP_FontAsset face = null)
    {
        var go = new GameObject("Scroll", typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        go.transform.SetParent(panel, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f); // inset look; also the drag hit area

        var text = MakeLabel(go.transform, "", fontSize, Vector2.zero, new Vector2(size.x - 20f, 10f), face);
        text.alignment = TextAlignmentOptions.TopLeft;
        var trt = (RectTransform)text.transform;
        trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -8f);
        text.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = go.GetComponent<ScrollRect>();
        scroll.content = trt;
        scroll.viewport = rt;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return text;
    }

    /// <summary>Reset a scroll-text back to the top (content pivot sits at the
    /// viewport top, so restoring the build-time offset shows the first line).</summary>
    static void ScrollToTop(TMP_Text t) =>
        ((RectTransform)t.transform).anchoredPosition = new Vector2(0f, -8f);

    void ToggleBriefPanel()
    {
        if (briefPanel == null || mission == null || mission.Current == null) return;
        bool show = !briefPanel.gameObject.activeSelf;
        if (show)
        {
            briefText.text = BriefToText(mission.Current);
            ScrollToTop(briefText);
        }
        briefPanel.gameObject.SetActive(show);
        if (show && resultPanel != null) resultPanel.gameObject.SetActive(false);
    }

    string BriefToText(Brief b)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("“" + b.title + "”");
        sb.AppendLine("Client: " + b.persona);
        sb.AppendLine("Budget: $" + b.budget.ToString("N0"));
        sb.AppendLine("Max floors: " + b.maxLevels);
        sb.Append("Rooms: ");
        for (int i = 0; i < b.requiredRooms.Count; i++)
        {
            var r = b.requiredRooms[i];
            sb.Append(BriefGenerator.TypeName(r.type) + " ×" + r.count);
            if (i < b.requiredRooms.Count - 1) sb.Append(", ");
        }
        sb.AppendLine();
        foreach (var l in b.lighting)
            sb.AppendLine("Light: the " + BriefGenerator.TypeName(l.room) + " needs a " + BriefGenerator.FacingName(l.facing) + "-facing window");
        if (b.special.Count > 0)
        {
            sb.AppendLine("Special requests:");
            foreach (var s in b.special) sb.AppendLine("· " + s);
        }
        sb.AppendLine();
        sb.AppendLine("Tip: furniture defines the room — a bed makes a bedroom, a sofa a living room, a table a dining room");
        return sb.ToString();
    }

    void SubmitMission()
    {
        if (mission == null || mission.Current == null) return;
        mission.Submit();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("— Inspection Report —");
        foreach (var r in mission.lastResults)
            sb.AppendLine((r.pass ? "✓ " : "× ") + r.item + " (" + r.points + "/" + r.maxPoints + ")  " + r.detail);
        sb.AppendLine();
        sb.AppendLine("Score: " + mission.lastScore + " / " + mission.lastMaxScore);
        sb.AppendLine();
        sb.AppendLine("Client: " + mission.VerdictText());
        resultText.text = sb.ToString();
        ScrollToTop(resultText);
        if (briefPanel != null) briefPanel.gameObject.SetActive(false);
        resultPanel.gameObject.SetActive(true);
    }

    void BuildFloorStylePanel(Transform root, float toolbarH)
    {
        var styles = bm != null && bm.grid != null ? bm.grid.GetFloorStyles() : null;
        if (styles == null || styles.Length == 0) return;
        const float iw = 96f, ih = 86f, gap = 8f;
        float total = styles.Length * iw + (styles.Length - 1) * gap;
        floorStylePanel = MakePanel(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, toolbarH + 44f), new Vector2(total + 20f, ih + 16f));
        for (int i = 0; i < styles.Length; i++)
        {
            var style = styles[i];
            float x = -total * 0.5f + iw * 0.5f + i * (iw + gap);
            var btn = MakeButton(floorStylePanel, "", new Vector2(x, 0f), new Vector2(iw, ih), () => bm.SelectFloorStyle(style.name));
            btn.GetComponent<Image>().color = new Color(style.color.r, style.color.g, style.color.b, 0.95f);
            var nameLabel = MakeLabel(btn.transform, string.IsNullOrEmpty(style.displayName) ? style.name : style.displayName, 18, new Vector2(0f, -ih * 0.5f + 14f), new Vector2(iw, 22f), fontSerif);
            nameLabel.raycastTarget = false;
            nameLabel.color = Color.black;
        }
        floorStylePanel.gameObject.SetActive(false);
    }

    void ToggleFloorStyles()
    {
        if (floorStylePanel != null) floorStylePanel.gameObject.SetActive(!floorStylePanel.gameObject.activeSelf);
    }

    void SetFloorStylesVisible(bool v)
    {
        if (floorStylePanel != null) floorStylePanel.gameObject.SetActive(v);
    }

    RectTransform BuildKindStylePanel(Transform root, float toolbarH, WallOpening kind)
    {
        var styles = bm != null && bm.grid != null ? bm.grid.GetWallKindStyles(kind) : null;
        if (styles == null || styles.Length == 0) return null;
        const float iw = 96f, ih = 86f, gap = 8f;
        float total = styles.Length * iw + (styles.Length - 1) * gap;
        var panel = MakePanel(root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, toolbarH + 44f), new Vector2(total + 20f, ih + 16f));
        for (int i = 0; i < styles.Length; i++)
        {
            var style = styles[i];
            float x = -total * 0.5f + iw * 0.5f + i * (iw + gap);
            var btn = MakeButton(panel, "", new Vector2(x, 0f), new Vector2(iw, ih), () => bm.SelectWallStyle(kind, style.name));
            btn.GetComponent<Image>().color = new Color(style.color.r, style.color.g, style.color.b, 0.95f);
            var nameLabel = MakeLabel(btn.transform, string.IsNullOrEmpty(style.displayName) ? style.name : style.displayName, 18, new Vector2(0f, -ih * 0.5f + 14f), new Vector2(iw, 22f), fontSerif);
            nameLabel.raycastTarget = false;
            nameLabel.color = Color.black;
        }
        panel.gameObject.SetActive(false);
        return panel;
    }

    static void TogglePanel(RectTransform p)
    {
        if (p != null) p.gameObject.SetActive(!p.gameObject.activeSelf);
    }

    static void HidePanel(RectTransform p)
    {
        if (p != null) p.gameObject.SetActive(false);
    }

    Sprite Icon(string name)
    {
        if (!iconCache.TryGetValue(name, out var s))
        {
            s = Resources.Load<Sprite>("Icons/" + name);
            if (s == null) Debug.LogWarning("GameUI: missing icon Resources/Icons/" + name);
            iconCache[name] = s;
        }
        return s;
    }

    /// <summary>Bare icon button: the white sprite itself is the whole visual
    /// and the raycast target — no box, no label. Selected/state tinting goes
    /// through Image.color (white sprite × color = any color).</summary>
    Button MakeIconButton(Transform parent, string iconName, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Icon_" + iconName, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = Icon(iconName);
        img.preserveAspect = true;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);
        return btn;
    }

    /// <summary>Invisible layout container: anchors like MakePanel but draws
    /// nothing, so bare icons sit directly over the 3D view.</summary>
    static RectTransform MakeBar(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject("Bar", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    RectTransform MakePanel(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject("Panel", typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = PanelColor;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    Button MakeButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick, TMP_FontAsset face = null)
    {
        var go = new GameObject("Btn_" + label, typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = ButtonColor;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        go.GetComponent<Button>().onClick.AddListener(onClick);
        var txt = MakeLabel(go.transform, label, 24, Vector2.zero, size, face);
        txt.raycastTarget = false;
        txt.textWrappingMode = TextWrappingModes.NoWrap; // button labels stay single-line
        return go.GetComponent<Button>();
    }

    TMP_Text MakeLabel(Transform parent, string content, float fontSize, Vector2 anchoredPos, Vector2 size, TMP_FontAsset face = null)
    {
        var go = new GameObject("Label", typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        var t = go.GetComponent<TextMeshProUGUI>();
        t.font = face != null ? face : fontMono;
        t.fontSize = fontSize;
        t.alignment = TextAlignmentOptions.Center;
        t.color = Color.white;
        t.text = content;
        // Small rects (level label, button labels) must never clip to nothing
        // when the CJK line height exceeds the rect.
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }
}
