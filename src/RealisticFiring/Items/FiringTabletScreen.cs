using DV.CabControls;
using UnityEngine;
using UnityEngine.UI;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The firing tablet's own screen: cached graphics on an ordinary mesh, so
    /// the display follows the held item's render pose. Its pages are a
    /// <see cref="FiringTabletPanel"/> on an isolated canvas, at the panel's
    /// own scale, under the tablet's native cab-control buttons.
    /// </summary>
    internal sealed class FiringTabletScreen : MonoBehaviour
    {
        internal const float PixelSize = 0.00056f;
        private const float SurfaceZ = -FiringTablet.Thickness * 0.5f - 0.0005f;
        private const float ButtonDepth = 0.004f;
        internal const float ButtonFront = SurfaceZ - ButtonDepth;
        private const int Width = FiringTabletPanel.Width, Height = FiringTabletPanel.Height;
        private Canvas canvas;
        private DV.Items.ItemTransparencyHandler transparency;
        private MeshRenderer surface;
        private Material screenMaterial;
        private Mesh screenMesh;
        private RenderTexture texture;
        private bool dirty = true;
        private bool visible;
        private float lastVisibleAt;
        internal Canvas RenderCanvas => canvas;
        private RectTransform root;
        private FiringTabletPanel panel;
        private FiringTabletGadget mounted;
        private DV.Customization.Gadgets.GadgetItem item;
        private MeshRenderer shell;
        // The page tabs, then the orders; made once, kept when the canvas is let go.
        private readonly GameObject[] buttons = new GameObject[FiringTabletPanel.Buttons];
        private System.Action<int, Rect, bool> setButton;
        private string dialKey;
        private float refreshAt;
        private int readPage = -1;
        private TrainCar readCar;

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
            var car = TargetCar();
            if (panel.UpdateAim()) dirty = true;
            if (Time.unscaledTime < refreshAt && readPage == state.Page && car == readCar)
                return;
            refreshAt = Time.unscaledTime + 1f;
            readPage = state.Page;
            readCar = car;
            panel.Read(car, state.Page, mounted != null);
            PlaceButtons();
            dirty = true;
        }

        internal void SelectPage(int page)
        {
            if (State != null)
                State.Page = page;
            // Stop invisible controls receiving touches before the next reading.
            panel?.HideOrders();
            for (var i = FiringTabletPanel.Pages; i < buttons.Length; i++)
                if (buttons[i] != null) buttons[i].SetActive(false);
            refreshAt = 0f;
        }

        private TrainCar TargetCar() => mounted != null ? mounted.TrainCar
            : item?.Item != null && item.Item.IsGrabbed() ? PlayerManager.Car : TrainCar.Resolve(gameObject);

        internal void GiveOrder(int order)
        {
            // Resolve again on every press: carrying a tablet into another cab
            // must never command the engine from the previous cached reading.
            if (State?.Page != FiringTabletGadget.FiremanPage || !FiringTabletPanel.GiveOrder(TargetCar(), order))
                return;
            refreshAt = 0f;
        }

        private void Pressed(int button)
        {
            if (button < FiringTabletPanel.Pages)
                SelectPage(button);
            else
                GiveOrder(button - FiringTabletPanel.Pages);
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
            scaler.referencePixelsPerUnit = FiringTabletPanel.ReferencePixelsPerUnit;
            root = screen.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(Width, Height);
            if (surface == null) BuildSurface();
            panel = new FiringTabletPanel(root, 1f, dialKey, "FIRING TABLET");
            PlaceButtons();
            item?.Item?.ReCacheRenderers();
        }

        private void PlaceButtons()
        {
            if (setButton == null)
                setButton = SetButton;
            panel?.PlaceButtons(setButton);
        }

        // The panel's button over its rectangle: made the first time, then shown or hidden.
        private void SetButton(int index, Rect area, bool on)
        {
            var button = buttons[index];
            if (button == null)
                button = buttons[index] = AddButton(index < FiringTabletPanel.Pages ? "page " + index : "order " + (index - FiringTabletPanel.Pages),
                    area, () => Pressed(index));
            if (button.activeSelf != on)
                button.SetActive(on);
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

        private GameObject AddButton(string name, Rect area, System.Action pressed)
        {
            var button = new GameObject("[tablet " + name + "]");
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
            FiringTabletSetup.ConfigurePageButton(spec, button);
            var use = button.AddComponent<FiringTabletPageButton>();
            use.Pressed = pressed;
            button.SetActive(true);
            return button;
        }

        private void ReleaseCanvas()
        {
            if (canvas != null)
            {
                Object.Destroy(canvas.gameObject);
                canvas = null;
                TabletScreenRenderer.Release();
            }
            panel?.Release();
            panel = null;
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
        internal System.Action Pressed;
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
                Pressed?.Invoke();
        }

        private void OnDestroy()
        {
            if (control != null)
                control.ValueChanged -= Changed;
        }
    }
}
