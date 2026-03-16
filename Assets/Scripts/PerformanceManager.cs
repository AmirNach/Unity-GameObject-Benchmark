using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime HUD with:
///   • Elapsed stopwatch (MM:SS)
///   • FPS counter (colour-coded)
///   • [Visuals ON/OFF]  — disables MeshRenderers + LineRenderers (logic keeps running)
///   • [Minimal Mode]    — additionally disables the camera and all scene lights
///                          (Screen Space Overlay canvas stays visible for stats)
///   • Interaction count (live)
///   • Average interaction duration (live)
/// </summary>
public class PerformanceManager : MonoBehaviour
{
    [Header("FPS Display")]
    public float fpsInterval = 0.3f;

    // ── cached renderer lists ──────────────────────────────────────────────
    private readonly List<MeshRenderer> _bodies = new();
    private readonly List<LineRenderer> _lines  = new();

    // ── UI refs ────────────────────────────────────────────────────────────
    private Image _fullscreenBg;   // solid background shown in Minimal Mode
    private Text  _timerText;
    private Text  _fpsText;
    private Text  _visBtnLabel;
    private Image _visBtnImage;
    private Text  _minBtnLabel;
    private Image _minBtnImage;
    private Text       _interactionsText;
    private Text       _avgDurText;
    private InputField _timeScaleInput;
    private InputField _smartInput;
    private InputField _dumbInput;

    // ── FPS tracking ───────────────────────────────────────────────────────
    private float _fpsAccum;
    private int   _fpsFrames;
    private float _fpsTimer;

    // ── stopwatch ─────────────────────────────────────────────────────────
    private float _elapsed = 0f;

    // ── state ──────────────────────────────────────────────────────────────
    private bool _visualsOn  = true;
    private bool _minimalOn  = false;

    // ── scene objects for minimal mode ────────────────────────────────────
    private Camera             _mainCam;
    private readonly List<Light> _sceneLights = new();

    // ── colours ───────────────────────────────────────────────────────────
    private static readonly Color ColGreen  = new Color(0.15f, 0.40f, 0.15f, 0.90f);
    private static readonly Color ColRed    = new Color(0.45f, 0.10f, 0.10f, 0.90f);
    private static readonly Color ColBlue   = new Color(0.10f, 0.20f, 0.45f, 0.90f);
    private static readonly Color ColPurple = new Color(0.30f, 0.10f, 0.40f, 0.90f);

    // ──────────────────────────────────────────────────────────────────────
    void Start()
    {
        BuildUI();
        StartCoroutine(CacheAfterSpawn());
    }

    IEnumerator CacheAfterSpawn()
    {
        while (FindObjectsOfType<Wanderer>().Length == 0)
            yield return null;

        yield return null; // extra frame — let all agents finish Start()

        CacheRenderers();
    }

    void CacheRenderers()
    {
        _bodies.Clear();
        _lines.Clear();

        foreach (var w in FindObjectsOfType<Wanderer>())
        {
            var mr = w.GetComponent<MeshRenderer>();
            if (mr) _bodies.Add(mr);

            foreach (var lr in w.GetComponentsInChildren<LineRenderer>(true))
                _lines.Add(lr);
        }

        Debug.Log($"[PerfManager] Cached {_bodies.Count} mesh + {_lines.Count} line renderers.");
    }

    // ── update ────────────────────────────────────────────────────────────
    void Update()
    {
        // Stopwatch
        _elapsed += Time.deltaTime;
        int totalSec = (int)_elapsed;
        _timerText.text = $"Time: {totalSec / 60:00}:{totalSec % 60:00}";

        // FPS
        _fpsAccum  += Time.unscaledDeltaTime;
        _fpsFrames++;
        _fpsTimer  += Time.unscaledDeltaTime;

        if (_fpsTimer >= fpsInterval)
        {
            float fps      = _fpsFrames / _fpsAccum;
            _fpsText.text  = $"FPS: {fps:F0}";
            _fpsText.color = fps >= 60 ? Color.green
                           : fps >= 30 ? Color.yellow
                           :             Color.red;
            _fpsAccum  = 0f;
            _fpsFrames = 0;
            _fpsTimer  = 0f;
        }

        // Interaction stats
        _interactionsText.text = $"Interactions: {Wanderer.TotalInteractions}";

        float avg = Wanderer.CompletedInteractions > 0
            ? Wanderer.TotalDurationSeconds / Wanderer.CompletedInteractions
            : 0f;
        _avgDurText.text = $"Avg Duration: {avg:F1}s";
    }

    // ── Visuals toggle ────────────────────────────────────────────────────
    public void ToggleVisuals()
    {
        _visualsOn = !_visualsOn;

        if (_bodies.Count == 0) CacheRenderers();

        Wanderer.VisualsEnabled = _visualsOn;
        foreach (var r in _bodies) if (r) r.enabled = _visualsOn;
        foreach (var l in _lines)  if (l) l.enabled = _visualsOn;

        if (_visBtnLabel != null)
            _visBtnLabel.text  = _visualsOn ? "Visuals: ON  ✓" : "Visuals: OFF  ✗";
        if (_visBtnImage != null)
            _visBtnImage.color = _visualsOn ? ColGreen : ColRed;
    }

    // ── Minimal Mode toggle ───────────────────────────────────────────────
    /// <summary>
    /// Disables the main camera and all scene lights.
    /// The Screen Space Overlay canvas is camera-independent and stays visible.
    /// All simulation logic continues running.
    /// </summary>
    public void ToggleMinimalMode()
    {
        _minimalOn = !_minimalOn;

        // Cache camera the first time (must be done before disabling it)
        if (_mainCam == null)
            _mainCam = Camera.main;

        // Cache lights the first time
        if (_sceneLights.Count == 0)
            foreach (var l in FindObjectsOfType<Light>())
                _sceneLights.Add(l);

        // Toggle camera rendering
        if (_mainCam != null)
            _mainCam.enabled = !_minimalOn;

        // Toggle all lights
        foreach (var l in _sceneLights)
            if (l) l.enabled = !_minimalOn;

        if (_minBtnLabel != null)
            _minBtnLabel.text  = _minimalOn ? "Minimal: ON  ✓" : "Minimal: OFF";
        if (_minBtnImage != null)
            _minBtnImage.color = _minimalOn ? ColPurple : ColBlue;

        // Show solid background so stats are readable against pure black
        if (_fullscreenBg != null)
            _fullscreenBg.gameObject.SetActive(_minimalOn);
    }

    // ── UI builder ────────────────────────────────────────────────────────
    void BuildUI()
    {
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        var root   = new GameObject("PerformanceUI");
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 99;
        root.AddComponent<CanvasScaler>();
        root.AddComponent<GraphicRaycaster>();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ── Full-screen background (hidden; shown in Minimal Mode) ─────────
        // Must be added FIRST so it renders behind all other UI elements.
        var bgGo = new GameObject("FullscreenBG");
        bgGo.transform.SetParent(root.transform, false);
        var bgRect = bgGo.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        _fullscreenBg       = bgGo.AddComponent<Image>();
        _fullscreenBg.color = new Color(0.04f, 0.04f, 0.04f, 0.97f); // near-black
        bgGo.SetActive(false); // hidden by default

        float x   = 12f;
        float y   = -10f;
        float gap = 10f;

        // ── Stopwatch ──────────────────────────────────────────────────────
        _timerText = MakeText(root, "Timer",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(210, 42),
            font, 30, FontStyle.Bold, TextAnchor.MiddleLeft, Color.cyan, "Time: 00:00");
        y -= 42 + gap;

        // ── FPS ────────────────────────────────────────────────────────────
        _fpsText = MakeText(root, "FPS",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(210, 42),
            font, 30, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, "FPS: --");
        y -= 42 + gap;

        // ── Visuals button ─────────────────────────────────────────────────
        (_visBtnLabel, _visBtnImage) = MakeButton(root, "VisBtn",
            new Vector2(x, y), new Vector2(210, 40),
            ColGreen, "Visuals: ON  ✓", font, ToggleVisuals);
        y -= 40 + gap;

        // ── Minimal Mode button ────────────────────────────────────────────
        (_minBtnLabel, _minBtnImage) = MakeButton(root, "MinBtn",
            new Vector2(x, y), new Vector2(210, 40),
            ColBlue, "Minimal: OFF", font, ToggleMinimalMode);
        y -= 40 + gap * 2;

        // ── Interaction count ──────────────────────────────────────────────
        _interactionsText = MakeText(root, "Interactions",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(240, 34),
            font, 20, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, "Interactions: 0");
        y -= 34 + gap;

        // ── Average duration ───────────────────────────────────────────────
        _avgDurText = MakeText(root, "AvgDur",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(240, 34),
            font, 20, FontStyle.Bold, TextAnchor.MiddleLeft, Color.white, "Avg Duration: 0.0s");
        y -= 34 + gap * 2;

        // ── Time Scale label + input ───────────────────────────────────────
        MakeText(root, "TSLabel",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(210, 28),
            font, 18, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(1f, 0.85f, 0.3f), "Time Scale:");
        y -= 28 + 4;

        _timeScaleInput = MakeInputField(root, "TSInput",
            new Vector2(x, y), new Vector2(210, 36), font, "1",
            InputField.ContentType.DecimalNumber);
        y -= 36 + gap * 2;

        // ── Agent Respawn ──────────────────────────────────────────────────
        MakeText(root, "AgentHeader",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(210, 26),
            font, 16, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Color(0.6f, 1f, 0.6f), "─── Agents ───");
        y -= 26 + 4;

        var spawner  = FindObjectOfType<AgentSpawner>();
        string initS = spawner != null ? spawner.smartCount.ToString() : "400";
        string initD = spawner != null ? spawner.dumbCount.ToString()  : "100";

        MakeText(root, "SmartLabel",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(210, 22),
            font, 15, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, "Smart agents:");
        y -= 22 + 2;
        _smartInput = MakeInputField(root, "SmartInput",
            new Vector2(x, y), new Vector2(210, 32), font, initS,
            InputField.ContentType.IntegerNumber);
        y -= 32 + 6;

        MakeText(root, "DumbLabel",
            new Vector2(0,1), new Vector2(0,1), new Vector2(0,1),
            new Vector2(x, y), new Vector2(210, 22),
            font, 15, FontStyle.Normal, TextAnchor.MiddleLeft, Color.white, "Dumb agents:");
        y -= 22 + 2;
        _dumbInput = MakeInputField(root, "DumbInput",
            new Vector2(x, y), new Vector2(210, 32), font, initD,
            InputField.ContentType.IntegerNumber);
        y -= 32 + gap;

        MakeButton(root, "RespawnBtn",
            new Vector2(x, y), new Vector2(210, 36),
            new Color(0.20f, 0.35f, 0.20f, 0.92f), "↺  Respawn", font, RespawnAgents);
    }

    // ── Time Scale apply ──────────────────────────────────────────────────
    void ApplyTimeScale(string val)
    {
        if (float.TryParse(val,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out float ts))
        {
            ts = Mathf.Clamp(ts, 0f, 1000f);
            Time.timeScale = ts;

            // Keep TimeManager Inspector field in sync
            var tm = FindObjectOfType<TimeManager>();
            if (tm != null) tm.timeScale = ts;

            _timeScaleInput.text = ts.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            // Bad input — restore to current actual value
            _timeScaleInput.text = Time.timeScale.ToString("F1",
                System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    // ── Agent Respawn ─────────────────────────────────────────────────────
    void RespawnAgents()
    {
        int smart = int.TryParse(_smartInput.text, out int s) ? Mathf.Max(0, s) : 0;
        int dumb  = int.TryParse(_dumbInput.text,  out int d) ? Mathf.Max(0, d) : 0;

        // Update input fields to show clamped values
        _smartInput.text = smart.ToString();
        _dumbInput.text  = dumb.ToString();

        // Reset simulation stats + stopwatch
        Wanderer.TotalInteractions     = 0;
        Wanderer.CompletedInteractions = 0;
        Wanderer.TotalDurationSeconds  = 0f;
        _elapsed = 0f;

        var spawner = FindObjectOfType<AgentSpawner>();
        if (spawner == null) { Debug.LogWarning("[PerfManager] AgentSpawner not found."); return; }

        spawner.Respawn(smart, dumb);
        StartCoroutine(RecacheAfterRespawn());
    }

    IEnumerator RecacheAfterRespawn()
    {
        // Wait for newly spawned agents to finish their Start()
        yield return null;
        yield return null;
        CacheRenderers();

        // Re-apply current visual state to new renderers
        if (!_visualsOn)
        {
            foreach (var r in _bodies) if (r) r.enabled = false;
            foreach (var l in _lines)  if (l) l.enabled = false;
        }
    }

    // ── helper: InputField ────────────────────────────────────────────────
    InputField MakeInputField(GameObject parent, string name,
        Vector2 pos, Vector2 size, Font font, string defaultVal,
        InputField.ContentType contentType = InputField.ContentType.DecimalNumber)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0, 1);
        rt.anchorMax        = new Vector2(0, 1);
        rt.pivot            = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        var bg    = go.AddComponent<Image>();
        bg.color  = new Color(0.12f, 0.12f, 0.12f, 0.92f);

        var field = go.AddComponent<InputField>();
        field.targetGraphic  = bg;
        field.contentType    = contentType;
        field.characterLimit = 6;

        // ── Text child ────────────────────────────────────────────────────
        var textGo   = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6, 2);
        textRect.offsetMax = new Vector2(-6, -2);
        var txt        = textGo.AddComponent<Text>();
        txt.font       = font;
        txt.fontSize   = 20;
        txt.fontStyle  = FontStyle.Bold;
        txt.color      = Color.white;
        txt.alignment  = TextAnchor.MiddleLeft;
        field.textComponent = txt;

        // ── Placeholder child ─────────────────────────────────────────────
        var phGo   = new GameObject("Placeholder");
        phGo.transform.SetParent(go.transform, false);
        var phRect = phGo.AddComponent<RectTransform>();
        phRect.anchorMin = Vector2.zero;
        phRect.anchorMax = Vector2.one;
        phRect.offsetMin = new Vector2(6, 2);
        phRect.offsetMax = new Vector2(-6, -2);
        var ph        = phGo.AddComponent<Text>();
        ph.font       = font;
        ph.fontSize   = 20;
        ph.fontStyle  = FontStyle.Italic;
        ph.color      = new Color(0.5f, 0.5f, 0.5f);
        ph.alignment  = TextAnchor.MiddleLeft;
        ph.text       = "1";
        field.placeholder = ph;

        field.text = defaultVal;
        field.onEndEdit.AddListener(ApplyTimeScale);

        return field;
    }

    // ── helper: button ────────────────────────────────────────────────────
    (Text label, Image img) MakeButton(GameObject root, string name,
        Vector2 pos, Vector2 size, Color bgColor, string text, Font font,
        UnityEngine.Events.UnityAction onClick)
    {
        var go   = new GameObject(name);
        go.transform.SetParent(root.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0, 1);
        rt.anchorMax        = new Vector2(0, 1);
        rt.pivot            = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        var img   = go.AddComponent<Image>();
        img.color = bgColor;

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor     = new Color(0.75f, 0.75f, 0.75f);
        btn.colors = colors;
        btn.onClick.AddListener(onClick);

        var label = MakeText(go, "Label",
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero,
            font, 17, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, text);

        return (label, img);
    }

    // ── helper: Text ──────────────────────────────────────────────────────
    Text MakeText(GameObject parent, string name,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size,
        Font font, int fontSize, FontStyle style,
        TextAnchor align, Color color, string text)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;

        var t = go.AddComponent<Text>();
        t.font      = font;
        t.fontSize  = fontSize;
        t.fontStyle = style;
        t.alignment = align;
        t.color     = color;
        t.text      = text;

        var sh = go.AddComponent<Shadow>();
        sh.effectColor    = new Color(0, 0, 0, 0.65f);
        sh.effectDistance = new Vector2(1.5f, -1.5f);

        return t;
    }
}
