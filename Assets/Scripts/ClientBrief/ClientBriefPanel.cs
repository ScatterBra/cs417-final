using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// Client Briefs &amp; Evaluation (owner: Li Ziyang, xiaotuzi7877).
/// Minimal first version: shows the client's name, a checklist of requirements,
/// and a "Requirements Met: X / Y" counter with a progress bar.
/// Requirements can be toggled by clicking them or pressing 1..9 (placeholder until
/// RoomAnalyzer-driven automatic evaluation is integrated via SetRequirementMet).
/// </summary>
public class ClientBriefPanel : MonoBehaviour
{
    [Header("Client Brief")]
    public string clientName = "Grandma's New Home";
    public List<string> requirements = new List<string>
    {
        "At least 1 bedroom",
        "At least 1 bathroom",
        "No stairs in the house",
    };

    [Header("Layout")]
    public RenderMode renderMode = RenderMode.ScreenSpaceOverlay;
    public Vector2 panelSize = new Vector2(460, 300);

    static readonly Color PanelColor = new Color(0.13f, 0.18f, 0.23f, 0.92f);
    static readonly Color TitleColor = new Color(0.98f, 0.89f, 0.72f);
    static readonly Color TextColor = new Color(0.86f, 0.89f, 0.92f);
    static readonly Color MetColor = new Color(0.55f, 0.85f, 0.70f);
    static readonly Color BarBgColor = new Color(1f, 1f, 1f, 0.12f);

    readonly List<bool> met = new List<bool>();
    readonly List<TextMeshProUGUI> rowLabels = new List<TextMeshProUGUI>();
    TextMeshProUGUI counterLabel;
    RectTransform barFill;
    bool built;

    public Canvas Canvas { get; private set; }
    public int MetCount { get { int n = 0; foreach (var m in met) if (m) n++; return n; } }
    public int TotalCount => requirements.Count;

    void Awake() => Build();

    /// <summary>Creates the Canvas UI under this object (idempotent).</summary>
    public void Build()
    {
        if (built) return;
        built = true;

        var canvasGo = new GameObject("ClientBriefCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(transform, false);
        Canvas = canvasGo.AddComponent<Canvas>();
        Canvas.renderMode = renderMode;
        Canvas.sortingOrder = 50;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        canvasGo.AddComponent<GraphicRaycaster>();

        // Panel (top-left corner)
        var panel = NewUI("ClientBriefPanel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
        panel.anchoredPosition = new Vector2(24, -24);
        panel.sizeDelta = panelSize;
        panel.gameObject.AddComponent<Image>().color = PanelColor;

        float y = -18;
        var tag = NewText("Tag", panel, "UIKEA / CLIENT BRIEF", 15, TextColor * 0.8f, ref y, 22);
        tag.fontStyle = FontStyles.UpperCase;
        NewText("ClientName", panel, clientName, 28, TitleColor, ref y, 44).fontStyle = FontStyles.Bold;

        met.Clear(); rowLabels.Clear();
        for (int i = 0; i < requirements.Count; i++)
        {
            met.Add(false);
            int index = i;
            var label = NewText("Requirement_" + (i + 1), panel, "", 20, TextColor, ref y, 32);
            var btn = label.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => ToggleRequirement(index));
            rowLabels.Add(label);
        }

        y -= 6;
        counterLabel = NewText("RequirementsCounter", panel, "", 20, TitleColor, ref y, 30);
        counterLabel.fontStyle = FontStyles.Bold;

        var barBg = NewUI("ProgressBar", panel);
        barBg.anchorMin = new Vector2(0, 1); barBg.anchorMax = new Vector2(1, 1); barBg.pivot = new Vector2(0, 1);
        barBg.offsetMin = new Vector2(20, y - 12); barBg.offsetMax = new Vector2(-20, y);
        barBg.gameObject.AddComponent<Image>().color = BarBgColor;
        barFill = NewUI("Fill", barBg);
        barFill.anchorMin = Vector2.zero; barFill.anchorMax = new Vector2(0, 1);
        barFill.offsetMin = barFill.offsetMax = Vector2.zero;
        barFill.gameObject.AddComponent<Image>().color = MetColor;

        panel.sizeDelta = new Vector2(panelSize.x, Mathf.Max(panelSize.y, -y + 32));

        EnsureEventSystem();
        Refresh();
    }

    public void ToggleRequirement(int index)
    {
        if (index < 0 || index >= met.Count) return;
        SetRequirementMet(index, !met[index]);
    }

    /// <summary>Hook for automatic evaluation (e.g. RoomAnalyzer results).</summary>
    public void SetRequirementMet(int index, bool value)
    {
        if (index < 0 || index >= met.Count) return;
        met[index] = value;
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < rowLabels.Count; i++)
        {
            rowLabels[i].text = (met[i] ? "<color=#8CD9B3>[x]</color> " : "[  ] ") + requirements[i];
            rowLabels[i].color = met[i] ? MetColor : TextColor;
        }
        counterLabel.text = $"Requirements Met: {MetCount} / {TotalCount}";
        float t = TotalCount == 0 ? 0 : (float)MetCount / TotalCount;
        barFill.anchorMax = new Vector2(t, 1);
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        for (int i = 0; i < Mathf.Min(9, met.Count); i++)
            if (kb[Key.Digit1 + i].wasPressedThisFrame) ToggleRequirement(i);
#endif
    }

    static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        es.AddComponent<InputSystemUIInputModule>();
#else
        es.AddComponent<StandaloneInputModule>();
#endif
    }

    static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TextMeshProUGUI NewText(string name, RectTransform parent, string text, float size, Color color, ref float y, float height)
    {
        var rt = NewUI(name, parent);
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0, 1);
        rt.offsetMin = new Vector2(20, y - height); rt.offsetMax = new Vector2(-20, y);
        y -= height;
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text; tmp.fontSize = size; tmp.color = color;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = true;
        return tmp;
    }
}
