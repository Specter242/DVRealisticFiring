using System.Collections.Generic;
using DV.CabControls;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace RealisticFiring.Items
{
    /// <summary>Cached screen graphics on an ordinary mesh, so the display follows the held item's render pose.</summary>
    internal sealed class FiringTabletScreen : MonoBehaviour
    {
        internal const float PixelSize = 0.00056f;
        private const float SurfaceZ = -FiringTablet.Thickness * 0.5f - 0.0005f;
        private const float ButtonDepth = 0.004f;
        internal const float ButtonFront = SurfaceZ - ButtonDepth;
        private const int Width = 540, Height = 462;
        private const float ReferencePixelsPerUnit = 100f;
        private const int MaxRows = 10;
        private Canvas canvas;
        private DV.Items.ItemTransparencyHandler transparency;
        private MeshRenderer surface;
        private Material screenMaterial;
        private Mesh screenMesh;
        private RenderTexture texture;
        private bool dirty = true;
        private bool visible;
        private bool controlsBuilt;
        private float lastVisibleAt;
        private int lastAim = -2;
        internal Canvas RenderCanvas => canvas;
        private RectTransform root;
        private FiringTabletGadget mounted;
        private DV.Customization.Gadgets.GadgetItem item;
        private MeshRenderer shell;
        private Text loco, advice, waiting;
        private readonly Image[] tabs = new Image[3], zones = new Image[9], zoneBorders = new Image[9];
        private readonly RawImage[] dials = new RawImage[4], smoke = new RawImage[4];
        private readonly Text[] tabNames = new Text[3], gaugeNames = new Text[4], gaugeValues = new Text[4], coal = new Text[9];
        private readonly RawImage[] gaugeMarks = new RawImage[4], stepChecks = new RawImage[5];
        private readonly Text[] labels = new Text[MaxRows], values = new Text[MaxRows];
        private readonly Text[] stepTitles = new Text[5], stepHow = new Text[5];
        private readonly Text[] stepNumbers = new Text[5];
        private Text front, door;
        private RawImage nextBar;
        private string dialKey;
        private float refreshAt;
        private int readPage = -1;
        private TrainCar readCar;
        private RealisticFirebox shownCoal;
        private int thin = -1;
        private readonly RawImage[] clinker = new RawImage[9];
        private readonly Dictionary<Texture2D, Sprite> panelSprites = new Dictionary<Texture2D, Sprite>();

        private FiringTabletGadget State => mounted != null ? mounted : item != null ? item.Gadget as FiringTabletGadget : null;

        private void Start()
        {
            mounted = GetComponent<FiringTabletGadget>();
            item = GetComponent<DV.Customization.Gadgets.GadgetItem>();
            shell = GetComponentInChildren<MeshRenderer>();
            transparency = GetComponent<DV.Items.ItemTransparencyHandler>();
            dialKey = "tablet-" + GetInstanceID() + "-";
            Build();
        }

        private void OnEnable()
        {
            refreshAt = 0f;
            lastVisibleAt = Time.unscaledTime;
            dirty = true;
            if (dialKey != null) Build();
        }

        private void OnDisable()
        {
            ReleaseTexture();
            ReleaseCanvas();
        }

        private void Update()
        {
            var state = State;
            if (canvas == null || state == null)
                return;
            var camera = PlayerManager.ActiveCamera;
            visible = camera != null && shell != null && shell.enabled && shell.gameObject.activeInHierarchy
                && (transparency == null || !transparency.IsTransparent)
                && (camera.transform.position - transform.position).sqrMagnitude < 64f;
            surface.enabled = visible && texture != null;
            if (!visible || (shell != null && !shell.isVisible))
            {
                if (Time.unscaledTime - lastVisibleAt > 10f) ReleaseTexture();
                return;
            }
            lastVisibleAt = Time.unscaledTime;
            // A mounted tablet never follows the player into a different engine.
            var car = mounted != null ? mounted.TrainCar
                : item?.Item != null && item.Item.IsGrabbed() ? PlayerManager.Car : TrainCar.Resolve(gameObject);
            UpdateAim();
            if (Time.unscaledTime < refreshAt && readPage == state.Page && car == readCar)
                return;
            refreshAt = Time.unscaledTime + 1f;
            readPage = state.Page;
            readCar = car;
            FireOverlay.Card card = null;
            shownCoal = null;
            if (FireboxRegistry.TryGetTended(car, out var fire))
            {
                shownCoal = fire;
                card = FireOverlay.Read(fire, state.Page);
            }
            else if (RealisticOilFirebox.TryGetTended(car, out var oil))
                card = FireOverlay.Read(oil, state.Page);
            Show(card, state.Page);
            UpdateAim();
            dirty = true;
        }

        internal void SelectPage(int page)
        {
            if (State != null)
                State.Page = page;
            refreshAt = 0f;
        }

        private void Build()
        {
            if (canvas != null) return;
            var screen = new GameObject("[firing tablet screen]", typeof(RectTransform), typeof(Canvas));
            canvas = screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            screen.transform.SetParent(TabletScreenRenderer.Acquire(), false);
            screen.transform.localPosition = Vector3.forward;
            screen.transform.localScale = Vector3.one * PixelSize;
            screen.layer = TabletScreenRenderer.Layer;
            canvas.worldCamera = TabletScreenRenderer.Camera;
            canvas.enabled = false;
            var scaler = screen.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 2f;
            scaler.referencePixelsPerUnit = ReferencePixelsPerUnit;
            root = screen.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(Width, Height);
            if (surface == null) BuildSurface();
            Picture("Background", new Rect(0, 0, Width, Height), OverlayArt.White, new Color(0.035f, 0.028f, 0.021f, 1f));
            Picture("Frame", new Rect(1, 1, Width - 2, 1), OverlayArt.White, OverlayArt.Soft);
            for (var i = 0; i < 3; i++)
            {
                var rect = new Rect(16 + i * 102, 12, 96, 26);
                tabs[i] = Panel("Page plate", rect, OverlayArt.PlateOff, Color.white);
                tabNames[i] = Words("Page", rect, 15, TextAnchor.MiddleCenter, OverlayArt.Cream);
                if (!controlsBuilt) AddButton(i, rect);
            }
            loco = Words("Locomotive", new Rect(330, 12, 194, 26), 16, TextAnchor.MiddleRight, OverlayArt.Soft);
            for (var i = 0; i < 4; i++)
            {
                var x = 16 + i * 128;
                dials[i] = Picture("Dial", new Rect(x + 5, 46, 112, 87), null, Color.white);
                gaugeNames[i] = Words("Gauge", new Rect(x, 124, 122, 20), 14, TextAnchor.MiddleCenter, OverlayArt.Cream);
                gaugeMarks[i] = Picture("Reading state", new Rect(x + 2, 155, 11, 11), null, Color.white);
                gaugeValues[i] = Words("Reading", new Rect(x + 16, 147, 106, 26), 18, TextAnchor.MiddleCenter, OverlayArt.Cream);
                smoke[i] = Picture("Smoke color", new Rect(x + 25, 173, 72, 7), OverlayArt.White, Color.clear);
            }
            front = Words("Grate front", new Rect(16, 180, 194, 16), 13, TextAnchor.MiddleCenter, OverlayArt.Soft);
            front.text = "FRONT";
            door = Words("Grate door", new Rect(16, 330, 194, 16), 13, TextAnchor.MiddleCenter, OverlayArt.Soft);
            door.text = "DOOR";
            for (var r = 0; r < 3; r++)
                for (var c = 0; c < 3; c++)
                {
                    var index = (int)GrateLayout.At(2 - r, c);
                    var rect = new Rect(16 + c * 66, 198 + r * 44, 62, 40);
                    zoneBorders[index] = Panel("Zone marker", new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), OverlayArt.Rounded, Color.clear);
                    zones[index] = Panel("Grate zone", rect, OverlayArt.Rounded, Color.white);
                    clinker[index] = Picture("Clinker", new Rect(rect.x + 4, rect.yMax - 7, rect.width - 8, 4), OverlayArt.White, new Color(0.6f, 0.63f, 0.65f));
                    coal[index] = Words("Coal kg", rect, 17, TextAnchor.MiddleCenter, OverlayArt.Cream);
                }
            for (var i = 0; i < MaxRows; i++)
            {
                labels[i] = Words("Row label", new Rect(), 16, TextAnchor.MiddleLeft, OverlayArt.Soft);
                values[i] = Words("Row value", new Rect(), 19, TextAnchor.MiddleRight, OverlayArt.Cream);
            }
            for (var i = 0; i < 5; i++)
            {
                stepTitles[i] = Words("Lighting step", new Rect(), 17, TextAnchor.MiddleLeft, OverlayArt.Cream);
                stepHow[i] = Words("Lighting instruction", new Rect(), 14, TextAnchor.MiddleLeft, OverlayArt.Soft);
                stepChecks[i] = Picture("Step completed", new Rect(), OverlayArt.Check, OverlayArt.Cream);
                stepNumbers[i] = Words("Step number", new Rect(), 17, TextAnchor.MiddleCenter, OverlayArt.Soft);
            }
            Picture("Advice background", new Rect(16, 388, 508, 64), OverlayArt.White, new Color(1f, 1f, 1f, 0.05f));
            nextBar = Picture("Advice level", new Rect(16, 388, 3, 64), OverlayArt.White, OverlayArt.Soft);
            advice = Words("Next step", new Rect(29, 394, 483, 52), 19, TextAnchor.UpperLeft, OverlayArt.Cream);
            advice.horizontalOverflow = HorizontalWrapMode.Wrap;
            waiting = Words("No engine", new Rect(40, 185, 460, 130), 24, TextAnchor.MiddleCenter, OverlayArt.Cream);
            waiting.horizontalOverflow = HorizontalWrapMode.Wrap;
            controlsBuilt = true;
            lastAim = -2;
            item?.Item?.ReCacheRenderers();
        }

        private void BuildSurface()
        {
            var display = new GameObject("[tablet display surface]");
            display.transform.SetParent(transform, false);
            display.transform.localPosition = new Vector3(0f, 0f, SurfaceZ);
            var w = Width * PixelSize * 0.5f;
            var h = Height * PixelSize * 0.5f;
            screenMesh = new Mesh { name = "RealisticFiring_TabletScreen" };
            screenMesh.vertices = new[] { new Vector3(-w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0), new Vector3(w, -h, 0) };
            screenMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            screenMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            screenMesh.RecalculateBounds();
            screenMesh.UploadMeshData(true);
            display.AddComponent<MeshFilter>().sharedMesh = screenMesh;
            screenMaterial = new Material(Shader.Find("Unlit/Texture")) { name = "RealisticFiring_TabletDisplay" };
            surface = display.AddComponent<MeshRenderer>();
            surface.sharedMaterial = screenMaterial;
            surface.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surface.receiveShadows = false;
            surface.enabled = false;
        }

        private void LateUpdate()
        {
            if (!visible || !dirty || canvas == null || surface == null || shell == null || !shell.isVisible)
                return;
            if (texture == null)
            {
                texture = new RenderTexture(Width * 2, Height * 2, 0, RenderTextureFormat.ARGB32)
                {
                    name = "RealisticFiring_TabletDisplay", filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp, useMipMap = true, autoGenerateMips = false,
                };
                texture.Create();
                screenMaterial.mainTexture = texture;
            }
            // One shared, disabled camera renders this canvas at the reading refresh or an aim change.
            var camera = TabletScreenRenderer.Camera;
            var previous = RenderTexture.active;
            try
            {
                canvas.enabled = true;
                camera.targetTexture = texture;
                Canvas.ForceUpdateCanvases();
                camera.Render();
                texture.GenerateMips();
                surface.enabled = true;
                dirty = false;
            }
            finally
            {
                canvas.enabled = false;
                camera.targetTexture = null;
                RenderTexture.active = previous;
            }
        }

        private void ReleaseTexture()
        {
            if (surface != null) surface.enabled = false;
            if (screenMaterial != null) screenMaterial.mainTexture = null;
            if (texture == null) return;
            texture.Release();
            Object.Destroy(texture);
            texture = null;
            dirty = true;
        }

        private void AddButton(int page, Rect area)
        {
            var button = new GameObject("[tablet page " + page + "]");
            button.SetActive(false);
            button.layer = FiringTablet.ControlLayer;
            button.transform.SetParent(transform, false);
            button.transform.localPosition = new Vector3((area.center.x - Width * 0.5f) * PixelSize,
                (Height * 0.5f - area.center.y) * PixelSize, ButtonFront + ButtonDepth * 0.5f);
            var collider = button.AddComponent<BoxCollider>();
            collider.size = new Vector3(area.width * PixelSize, area.height * PixelSize, ButtonDepth);
            // Hover casts include triggers; tabs must never push controls or loose items.
            collider.isTrigger = true;
            var spec = button.AddComponent<DV.CabControls.Spec.Button>();
            spec.colliderGameObjects = new[] { button };
            spec.createRigidbody = false;
            spec.useJoints = false;
            var use = button.AddComponent<FiringTabletPageButton>();
            use.Screen = this;
            use.Page = page;
            button.SetActive(true);
        }

        private void Show(FireOverlay.Card card, int page)
        {
            var hasData = card != null;
            waiting.gameObject.SetActive(!hasData);
            waiting.text = mounted != null
                ? "No supported steam engine\nThis car has no firing readings."
                : "No supported steam engine\nCarry the tablet aboard a locomotive to read its fire.";
            loco.text = hasData ? card.Loco : "FIRING TABLET";
            for (var i = 0; i < 3; i++)
            {
                tabs[i].sprite = PanelSprite(i == page ? OverlayArt.PlateOn : OverlayArt.PlateOff);
                tabNames[i].color = i == page ? OverlayArt.Cream : OverlayArt.Dim;
                tabNames[i].text = i == 0 ? (hasData && card.Oil ? "BURNER" : "FIRE") : i == 1 ? "WATER" : "ENGINE";
            }
            for (var i = 0; i < 4; i++)
            {
                dials[i].gameObject.SetActive(hasData);
                gaugeNames[i].gameObject.SetActive(hasData);
                gaugeValues[i].gameObject.SetActive(hasData);
                gaugeMarks[i].gameObject.SetActive(hasData);
                smoke[i].gameObject.SetActive(hasData && card.Gauges[i].Smoke.HasValue);
                if (!hasData)
                    continue;
                var gauge = card.Gauges[i];
                dials[i].texture = OverlayArt.Dial(dialKey + i, gauge.Value, gauge.Band0, gauge.Band1, gauge.Level);
                gaugeNames[i].text = gauge.Label;
                gaugeValues[i].text = gauge.Text;
                gaugeMarks[i].texture = OverlayArt.Mark(gauge.Level);
                gaugeMarks[i].color = OverlayArt.LevelColor(gauge.Level);
                smoke[i].color = gauge.Smoke ?? Color.clear;
            }
            var grate = hasData && card.ZoneCoal != null;
            thin = grate ? card.Thin : -1;
            front.gameObject.SetActive(grate);
            door.gameObject.SetActive(grate);
            for (var i = 0; i < 9; i++)
            {
                zones[i].gameObject.SetActive(grate);
                zoneBorders[i].gameObject.SetActive(grate);
                clinker[i].gameObject.SetActive(grate && card.ZoneClinker[i] > 0.1f);
                coal[i].gameObject.SetActive(grate);
                if (!grate)
                    continue;
                zones[i].color = OverlayArt.Ember(card.ZoneGlow[i]);
                coal[i].text = card.ZoneCoal[i].ToString("F1") + (card.Thin == i ? " ▲" : "");
                coal[i].color = card.ZoneGlow[i] > 0.72f ? OverlayArt.Ink : OverlayArt.Cream;
                clinker[i].rectTransform.sizeDelta = new Vector2(54f * Mathf.Clamp(card.ZoneClinker[i], 0.15f, 1f), 4f);
            }
            var rows = hasData ? card.Rows.Count : 0;
            var rowHeight = Mathf.Min(32f, 196f / Mathf.Max(1, rows));
            var left = grate ? 238f : 16f;
            var width = 524f - left;
            for (var i = 0; i < MaxRows; i++)
            {
                labels[i].gameObject.SetActive(i < rows);
                values[i].gameObject.SetActive(i < rows);
                if (i >= rows)
                    continue;
                var row = card.Rows[i];
                Place(labels[i].rectTransform, new Rect(left, 180 + i * rowHeight, width * 0.32f, rowHeight));
                Place(values[i].rectTransform, new Rect(left + width * 0.33f, 180 + i * rowHeight, width * 0.67f, rowHeight));
                labels[i].text = row.Label;
                values[i].text = row.Value;
                values[i].color = row.Level.HasValue ? OverlayArt.LevelColor(row.Level.Value) : OverlayArt.Cream;
            }
            var y = 182f;
            for (var i = 0; i < 5; i++)
            {
                var exists = hasData && card.Steps != null && i < card.Steps.Count;
                stepTitles[i].gameObject.SetActive(exists);
                stepHow[i].gameObject.SetActive(exists && card.Steps[i].Current);
                stepChecks[i].gameObject.SetActive(exists && card.Steps[i].Done);
                stepNumbers[i].gameObject.SetActive(exists && !card.Steps[i].Done);
                if (!exists)
                    continue;
                var step = card.Steps[i];
                Place(stepTitles[i].rectTransform, new Rect(48, y, 476, 26));
                Place(stepChecks[i].rectTransform, new Rect(20, y + 6, 14, 14));
                Place(stepNumbers[i].rectTransform, new Rect(16, y, 22, 26));
                stepNumbers[i].text = (i + 1).ToString();
                stepNumbers[i].color = step.Current ? OverlayArt.Caution : OverlayArt.Soft;
                stepTitles[i].text = step.Title;
                stepTitles[i].color = step.Current ? OverlayArt.Caution : step.Done ? OverlayArt.Dim : OverlayArt.Soft;
                y += 27f;
                if (step.Current)
                {
                    Place(stepHow[i].rectTransform, new Rect(48, y - 4, 476, 20));
                    stepHow[i].text = step.How;
                    y += 18f;
                }
            }
            advice.text = hasData ? card.Advice.Text : "F6 remains available as a screen overlay.";
            nextBar.color = hasData ? OverlayArt.LevelColor(card.Advice.Level) : OverlayArt.Soft;
        }

        private void UpdateAim()
        {
            if (shownCoal == null || zones[0] == null || !zones[0].gameObject.activeSelf)
                return;
            var aim = shownCoal.HoverAim;
            var aimed = aim.HasValue && aim.Value.IsLiveAt(Time.time) ? (int)aim.Value.Zone : -1;
            if (aimed != lastAim) { dirty = true; lastAim = aimed; }
            for (var i = 0; i < 9; i++)
                zoneBorders[i].color = i == aimed ? OverlayArt.Steam
                    : i == thin ? OverlayArt.Caution : Color.clear;
        }

        private RectTransform Rect(string name, Rect area)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = TabletScreenRenderer.Layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root, false);
            Place(rect, area);
            return rect;
        }

        private static void Place(RectTransform rect, Rect area)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(area.x, -area.y);
            rect.sizeDelta = area.size;
        }

        private RawImage Picture(string name, Rect area, Texture texture, Color color)
        {
            var image = Rect(name, area).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Image Panel(string name, Rect area, Texture2D texture, Color color)
        {
            var image = Rect(name, area).gameObject.AddComponent<Image>();
            image.sprite = PanelSprite(texture);
            image.type = Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Sprite PanelSprite(Texture2D texture)
        {
            if (panelSprites.TryGetValue(texture, out var sprite) && sprite != null)
                return sprite;
            // Match F6's five-pixel slicing, keeping corner size and border
            // width fixed. RawImage stretched these assets across the whole button
            // or cell, enlarging their antialiased edges into a soft band.
            sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), ReferencePixelsPerUnit, 0, SpriteMeshType.FullRect,
                new Vector4(5f, 5f, 5f, 5f), false);
            sprite.name = "RealisticFiring_TabletPanel";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            panelSprites[texture] = sprite;
            return sprite;
        }

        private Text Words(string name, Rect area, int size, TextAnchor alignment, Color color)
        {
            var text = Rect(name, area).gameObject.AddComponent<Text>();
            text.font = OverlayStyle.Font != null ? OverlayStyle.Font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 12;
            text.resizeTextMaxSize = size;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        private void ReleaseCanvas()
        {
            if (canvas != null)
            {
                Object.Destroy(canvas.gameObject);
                canvas = null;
                TabletScreenRenderer.Release();
            }
            foreach (var sprite in panelSprites.Values)
                if (sprite != null) Object.Destroy(sprite);
            panelSprites.Clear();
            if (dialKey == null)
                return;
            for (var i = 0; i < 4; i++)
                OverlayArt.ReleaseDial(dialKey + i);
        }

        private void OnDestroy()
        {
            ReleaseTexture();
            ReleaseCanvas();
            if (screenMaterial != null) Object.Destroy(screenMaterial);
            if (screenMesh != null) Object.Destroy(screenMesh);
        }
    }

    internal static class TabletScreenRenderer
    {
        // Unused by the stock game's cameras; only the private screen camera sees this layer.
        internal const int Layer = 30;
        private static GameObject host;
        private static Camera camera;
        private static int users;
        internal static Camera Camera => camera;

        internal static Transform Acquire()
        {
            if (host == null)
            {
                host = new GameObject("[RealisticFiring tablet screen renderer]");
                Object.DontDestroyOnLoad(host);
                camera = host.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = new Vector3(0f, 0f, -1f);
                camera.orthographic = true;
                camera.orthographicSize = 462f * FiringTabletScreen.PixelSize * 0.5f;
                camera.aspect = 540f / 462f;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << Layer;
                camera.renderingPath = RenderingPath.Forward;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.useOcclusionCulling = false;
            }
            users++;
            // The canvas plane sits one meter in front of the camera, independently of the physical tablet.
            return host.transform;
        }

        internal static void Release()
        {
            if (--users > 0) return;
            if (host != null) Object.Destroy(host);
            host = null;
            camera = null;
            users = 0;
        }
    }

    internal sealed class FiringTabletPageButton : MonoBehaviour
    {
        internal FiringTabletScreen Screen;
        internal int Page;
        private ControlImplBase control;

        private void Start()
        {
            control = GetComponent<ControlImplBase>();
            if (control != null)
                control.ValueChanged += Changed;
        }

        private void Changed(ValueChangedEventArgs change)
        {
            if (change.newValue >= 0.5f && change.oldValue < 0.5f)
                Screen.SelectPage(Page);
        }

        private void OnDestroy()
        {
            if (control != null)
                control.ValueChanged -= Changed;
        }
    }
}
