using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Interaction;
using DV.Items;
using DV.Shops;
using DV.Utils;
using HarmonyLib;
using RealisticFiring.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Items
{
    internal enum StartingFuelKind { Wood, Rag, WoodCrate, RagBox }

    /// <summary>
    /// Saved stock items with their own bodies and fuel state. Each starts
    /// from a copy of the stock shovel's prefab (the game's item plumbing:
    /// grabbing, storage, saving), stripped of everything that makes it a
    /// shovel: its scoop and coal, its mount anchor and snap points, its
    /// shop restocking, its metal knock, and its grip.
    /// </summary>
    internal static class FireStartingItems
    {
        internal const string Prefix = "RealisticFiring_Starting_";
        internal const int ContainerCapacity = 12;
        private static readonly Dictionary<StartingFuelKind, GameObject> templates = new Dictionary<StartingFuelKind, GameObject>();
        private static readonly string[] ShovelBehaviours = { "Shovel", "ShovelAudio", "ShovelNonPhysicalCoal", "SnappableItem", "ShopRestocker" };
        private static readonly string[] ShovelChildren = { "model", "scoop pivot", "[spawn anchors]", "[visual coal anchor]", "stick", "handle", "[ShovelMountAnchor]" };

        internal static string Name(StartingFuelKind kind) => kind == StartingFuelKind.Wood ? "Split firewood"
            : kind == StartingFuelKind.Rag ? "Lighting rag" : kind == StartingFuelKind.WoodCrate ? "Firewood crate" : "Rag tin";
        internal static bool IsContainer(StartingFuelKind kind) => kind == StartingFuelKind.WoodCrate || kind == StartingFuelKind.RagBox;

        /// <summary>
        /// How each is held and used in VR (SeanJAnimations and Blaze221,
        /// Nexus, 2026-11-12: a crate could be carried, but nothing came out
        /// of it). The shovel these are made from is a two-handed pole whose
        /// use is desktop-only, so a VR hand could never use a crate. Each is
        /// carried in one hand, as the game's own crates are; a crate or tin
        /// used while held (the trigger) puts a piece on top of it; and a
        /// loose piece outranks its crate for the other hand, so it takes the
        /// piece rather than the crate. A piece's use stays desktop-only: it
        /// lays the piece by the door, which a VR hand does itself.
        /// </summary>
        internal static (DV.CabControls.Spec.ItemControllerAttachMethod Attach, DV.CabControls.Spec.ItemUseApproach Use, int Priority) VrGrab(StartingFuelKind kind) =>
            IsContainer(kind)
                ? (DV.CabControls.Spec.ItemControllerAttachMethod.ReparentToController, DV.CabControls.Spec.ItemUseApproach.OneShot, 0)
                : (DV.CabControls.Spec.ItemControllerAttachMethod.ReparentToController, DV.CabControls.Spec.ItemUseApproach.OneShotNonVROnly, 1);
        internal static StartingFuelKind Content(StartingFuelKind kind) => kind == StartingFuelKind.WoodCrate ? StartingFuelKind.Wood : StartingFuelKind.Rag;
        internal static bool IsFuel(string path) => path != null && path.StartsWith(Prefix, StringComparison.Ordinal);
        internal static GameObject Load(string path) => Enum.TryParse(path.Substring(Prefix.Length), out StartingFuelKind kind)
            && Enum.IsDefined(typeof(StartingFuelKind), kind) ? Template(kind) : null;

        /// <summary>A container's own mass, kg: a slatted pine crate, a sheet-steel tin.</summary>
        internal static float EmptyMass(StartingFuelKind kind) => kind == StartingFuelKind.WoodCrate ? 3.5f : kind == StartingFuelKind.RagBox ? 2.2f : 0f;
        internal static float ContentMass(StartingFuelKind kind) => kind == StartingFuelKind.WoodCrate ? StarterFuel.PieceMass : 0.05f;

        // Where each is held, against the camera's hand anchor (FovBasedNonVRGrabAnchor):
        // a split carried by its middle, end forward; a rag bunched in the
        // fist; a crate or tin held low in front, both hands.
        private static (Vector3 Position, Vector3 Rotation) Grip(StartingFuelKind kind) =>
            kind == StartingFuelKind.Wood ? (new Vector3(0.02f, -0.02f, 0.06f), new Vector3(8f, -12f, 0f))
            : kind == StartingFuelKind.Rag ? (new Vector3(0f, -0.01f, 0.05f), new Vector3(10f, 25f, 0f))
            : (new Vector3(0f, -0.20f, 0.30f), new Vector3(12f, 0f, 0f));

        internal static GameObject Template(StartingFuelKind kind)
        {
            if (templates.TryGetValue(kind, out var cached) && cached != null) return cached;
            var source = Resources.Load<GameObject>("shovel");
            if (source == null) return null;
            var holder = new GameObject("[RF starting fuel template]"); holder.SetActive(false); Object.DontDestroyOnLoad(holder);
            var copy = Object.Instantiate(source, holder.transform, false);
            copy.name = Prefix + kind;
            foreach (var component in copy.GetComponents<MonoBehaviour>().Where(c => c != null && ShovelBehaviours.Contains(c.GetType().Name)).ToArray())
                Object.DestroyImmediate(component);
            foreach (var name in ShovelChildren)
            {
                var child = copy.transform.Find(name);
                if (child != null) Object.DestroyImmediate(child.gameObject);
            }
            foreach (var col in copy.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(col);

            var body = StartingFuelBody.Build(copy.transform, kind, 0);
            var colliders = StartingFuelBody.AddColliders(copy, kind);
            var spec = copy.GetComponent<DV.CabControls.Spec.Item>();
            spec.colliderGameObjects = colliders.Select(c => c.gameObject).Distinct().ToArray();
            spec.rigidbodyMass = kind == StartingFuelKind.Wood ? StarterFuel.PieceMass : kind == StartingFuelKind.Rag ? 0.05f
                : EmptyMass(kind) + ContainerCapacity * ContentMass(kind);
            spec.preventNestedGrabWhenUnGrabbed = false;
            var grab = VrGrab(kind);
            spec.controllerAttachMethod = grab.Attach;
            spec.precisionGrab = true;
            spec.itemUseApproach = grab.Use;
            spec.interactionPriority = grab.Priority;
            // Not a shovel: no shovel mount, hanger, or belt; no coal; its own knock.
            spec.allowedSnapPointTypes = DV.CabControls.Spec.SnapPointTypes.None;
            spec.coalAmount = 0f;
            var knock = StartingFuelBody.Knock(kind);
            spec.collision = knock;
            spec.itemCollisionSoundCategory = knock != null ? DV.CabControls.Spec.ItemCollisionSoundCategory.Generic
                : DV.CabControls.Spec.ItemCollisionSoundCategory.NeverPlayCollisionSounds;
            var sound = copy.GetComponent<CollisionSound>();
            if (sound != null)
            {
                sound.sound = knock;
                sound.InitializeCollisionSoundCategory(spec.itemCollisionSoundCategory, DV.CabControls.Spec.ItemCollisionSoundCategory.NeverPlayCollisionSounds);
            }
            var anchor = copy.GetComponent<FovBasedNonVRGrabAnchor>();
            if (anchor != null)
            {
                var grip = Grip(kind);
                anchor.localPosition = grip.Position;
                anchor.localRotation = grip.Rotation;
                anchor.fovRotationXOffset = AnimationCurve.Constant(30f, 120f, 0f);
                anchor.fovDistanceOffset = AnimationCurve.Constant(30f, 120f, 0f);
            }

            var inventory = copy.GetComponent<InventoryItemSpec>();
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(inventory, copy.name);
            inventory.NameOverride = Name(kind);
            inventory.IsEssential = false; inventory.ImmuneToDumpster = false;
            inventory.PreviewBounds = StartingFuelBody.Bounds(kind);
            StartingFuelBody.SetIcon(inventory, kind);
            inventory.PreviewPrefab = StartingFuelBody.Build(holder.transform, kind, 0).gameObject;
            if (copy.GetComponent<ItemSaveData>() == null) copy.AddComponent<ItemSaveData>();
            var fuel = copy.AddComponent<StartingFuelItem>(); fuel.Kind = kind;
            if (kind == StartingFuelKind.Rag)
            {
                // The oil can's and lighter's target: the rag's own body, and
                // round it for the lighter's flame.
                var ignition = copy.AddComponent<SphereCollider>(); ignition.radius = 0.10f; ignition.isTrigger = true;
                fuel.IgnitionCollider = ignition;
                var target = copy.GetComponent<ItemUseTarget>() ?? copy.AddComponent<ItemUseTarget>();
                target.targetColliders = colliders.Concat(new Collider[] { ignition }).ToArray();
            }
            if (kind == StartingFuelKind.Wood || kind == StartingFuelKind.Rag)
            {
                if (copy.GetComponent<StartingFuelPlace>() == null) copy.AddComponent<StartingFuelPlace>();
                var tip = new GameObject(StartingFuelPlace.TipName).transform;
                tip.SetParent(copy.transform, false);
                tip.localPosition = kind == StartingFuelKind.Wood ? new Vector3(0f, 0f, 0.15f) : Vector3.zero;
            }
            Main.Mod.Logger.Log($"Starting fuel '{kind}' made from the shovel: " + string.Join(", ", copy.GetComponents<Component>().Select(c => c.GetType().Name))
                                + (StartingFuelBody.Authored ? "; authored bodies" : "; no effects bundle, simple bodies"));
            templates[kind] = copy;
            return copy;
        }

        /// <summary>
        /// A new item. Station stock is nobody's until a player picks it up
        /// (<see cref="StartingFuelItem"/> claims it then): unclaimed stock is
        /// neither saved nor swept to Lost &amp; Found.
        /// </summary>
        internal static StartingFuelItem Spawn(StartingFuelKind kind, Vector3 position, Quaternion rotation, bool belongsToPlayer = true)
        {
            var template = Template(kind);
            if (template == null) return null;
            var copy = Object.Instantiate(template, position, rotation);
            copy.name = Prefix + kind;
            copy.GetComponent<InventoryItemSpec>().BelongsToPlayer = belongsToPlayer;
            if (belongsToPlayer)
                SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(copy.GetComponent<ItemBase>());
            return copy.GetComponent<StartingFuelItem>();
        }

        internal static void Register()
        {
            var items = DV.Globals.G?.Items?.items;
            if (items == null) return;
            foreach (StartingFuelKind kind in Enum.GetValues(typeof(StartingFuelKind)))
            {
                var spec = Template(kind)?.GetComponent<InventoryItemSpec>();
                if (spec != null && !items.Contains(spec)) items.Add(spec);
            }
        }
    }

    internal sealed class StartingFuelItem : MonoBehaviour, IIgnitable, IInventoryItemLocalizer
    {
        internal static readonly HashSet<StartingFuelItem> Live = new HashSet<StartingFuelItem>();
        internal static Action<StartingFuelItem, int> SendAction;
        /// <summary>The Multiplayer bridge: whether another player holds or carries this item.</summary>
        internal static Func<StartingFuelItem, bool> HeldRemotely;
        internal const int OilAction = 0, IgniteAction = 1, UseAction = 2, TakeAction = 3;
        private void OnEnable() => Live.Add(this);
        private void OnDisable()
        {
            Live.Remove(this);
            // Multiplayer hides the host's copy while another player holds or
            // stows it: when it comes back, it was let go by a remote hand.
            if (!Consumed) { WasHeld = true; WasHeldLocally = false; }
        }
        public StartingFuelKind Kind;
        public SphereCollider IgnitionCollider;
        internal readonly RagFuel Rag = new RagFuel();
        internal int Count = FireStartingItems.ContainerCapacity;
        internal string SupplyId;
        internal bool Consumed;
        internal ItemBase Item => item;
        private ItemBase item;
        private ItemSaveData save;
        internal Vector3 Previous;
        internal bool HavePrevious;
        // Held in the last frame seen, and whether by this machine's player.
        internal bool WasHeld, WasHeldLocally;
        private Transform body;
        private Renderer[] renderers;
        private MaterialPropertyBlock appearance;
        private Transform[] contents;
        private int shownCount = -1;
        private Light flameLight;
        private GameObject flame;
        private float seed;
        private float lastShown = -1f;
        private Collider reach;
        private readonly float[] actionAt = new float[4];

        /// <summary>
        /// Its rigidbody: the game's item's, or its own until the game has
        /// made its cab item (ItemBase.ItemRigidbody is null until then: an
        /// eject that set only that left a fresh piece flying back in through
        /// the door, preview 1.18.0's doorway check).
        /// </summary>
        internal Rigidbody Body
        {
            get
            {
                var game = item != null ? item.ItemRigidbody : null;
                return game != null ? game : GetComponent<Rigidbody>();
            }
        }

        /// <summary>Its velocity at the last physics step, before a strike in this one.</summary>
        internal Vector3 LastVelocity;

        private void FixedUpdate()
        {
            if (Consumed) return;
            var rb = Body;
            if (rb != null) LastVelocity = rb.velocity;
        }

        // Struck something: in a firebox's doorway, thrown in, it goes in
        // (FuelFireboxIntake.Strike). The host decides, as for every entry.
        private void OnCollisionEnter(Collision collision)
        {
            if (Consumed || Held || Net.IsClient || collision == null) return;
            FuelFireboxIntake.Strike(this);
        }

        internal bool HeldLocally => item != null && item.IsGrabbed();
        internal bool Held => HeldLocally || HeldRemotely != null && HeldRemotely(this);

        private void Request(int action)
        {
            if (Time.unscaledTime < actionAt[action]) return;
            actionAt[action] = Time.unscaledTime + 0.25f;
            SendAction?.Invoke(this, action);
        }
        public bool Ignited => Kind == StartingFuelKind.Rag && Rag.Lit;
        public bool IgnitionAllowed => Kind == StartingFuelKind.Rag && Rag.Oil > 0f && !Rag.Lit && !LevelInfo.IsUnderWater(transform.position);
        public SphereCollider OverlapInteractionCollider => IgnitionCollider;
        public Transform InteractionPoint => transform;
        public Transform GetTransform() => transform;
        public bool Ignite(float strength)
        {
            if (!IgnitionAllowed) return false;
            if (Net.IsClient) { Request(IgniteAction); return true; }
            return Rag.Ignite();
        }
        internal void Oil()
        {
            if (Net.IsClient) { Request(OilAction); return; }
            if (Kind == StartingFuelKind.Rag) Rag.Soak(RagFuel.OilCapacity);
        }
        internal void ApplyClientTake() => Request(TakeAction);
        internal void ApplyAction(int action)
        {
            if (Net.IsClient) return;
            if (action == OilAction) Oil();
            else if (action == IgniteAction) Ignite(1f);
            else if (action == UseAction) Use();
            else if (action == TakeAction) TakeOut(null);
        }
        private void Awake()
        {
            item = GetComponent<ItemBase>();
            save = GetComponent<ItemSaveData>();
            save.ItemSaveDataRequested += Save;
            save.ItemSaveDataLoaded += Restore;
            body = transform.Find(StartingFuelBody.BodyName);
            appearance = new MaterialPropertyBlock();
            seed = UnityEngine.Random.value;
            // Each loose piece its own split or crumple.
            if (body != null && (Kind == StartingFuelKind.Wood || Kind == StartingFuelKind.Rag))
                StartingFuelBody.Vary(body, GetComponent<MeshCollider>(), Kind, (int)(seed * 3f) % 3);
            if (body != null) renderers = body.GetComponentsInChildren<Renderer>(true);
            if (FireStartingItems.IsContainer(Kind) && body != null)
                contents = Enumerable.Range(0, FireStartingItems.ContainerCapacity).Select(i => body.Find(StartingFuelBody.SlotName(i))).ToArray();
            GetComponent<InventoryItemSpec>().NameOverride = FireStartingItems.Name(Kind);
            reach = transform.Find(StartingFuelBody.ContentsName)?.GetComponent<Collider>();
        }
        private void Start()
        {
            item = GetComponent<ItemBase>();
            if (item != null) { item.Used += Use; item.Grabbed += OnGrabbed; }
            if (Kind == StartingFuelKind.Rag)
            {
                var light = new GameObject("rag flame"); light.transform.SetParent(transform, false);
                light.transform.localPosition = Vector3.up * 0.06f;
                flameLight = light.AddComponent<Light>(); flameLight.color = new Color(1f, 0.5f, 0.15f); flameLight.range = 1.6f;
                flameLight.shadows = LightShadows.None;
                flame = StartingFuelBody.AddFlame(light.transform, 0.06f, 0.16f);
            }
        }
        private void OnDestroy()
        {
            if (item != null) { item.Used -= Use; item.Grabbed -= OnGrabbed; }
            // Its own material copies (SetState own: true) go with it.
            if (lastShown >= 0f && renderers != null)
                foreach (var r in renderers)
                    if (r != null) foreach (var m in r.sharedMaterials) if (m != null) Destroy(m);
            if (save != null) { save.ItemSaveDataRequested -= Save; save.ItemSaveDataLoaded -= Restore; }
        }

        // Station stock becomes the player's when picked up: saved, and like
        // any of their things swept to Lost & Found when left behind.
        private void OnGrabbed(ControlImplBase _)
        {
            var spec = GetComponent<InventoryItemSpec>();
            if (spec == null || spec.BelongsToPlayer || Net.IsClient) return;
            spec.BelongsToPlayer = true;
            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(item);
        }

        // A held container's use: a piece out on top of it, for VR hands (the
        // trigger, VrGrab) and anyone carrying it (a grounded one hands a
        // piece straight to the hand, StartingFuelTake).
        private void Use()
        {
            if (!FireStartingItems.IsContainer(Kind)) return;
            if (Net.IsClient) { Request(UseAction); return; }
            TakeOut(null);
        }

        /// <summary>Takes one piece out, at the container's mouth; null when it is empty (host only).</summary>
        internal StartingFuelItem TakeOut(Vector3? at)
        {
            if (!FireStartingItems.IsContainer(Kind) || Net.IsClient) return null;
            if (Count <= 0)
            {
                FireController.Announce(Kind == StartingFuelKind.WoodCrate ? "The crate is empty. Refill it at a service-track supply." : "The tin is empty. Refill it at a service-track supply.");
                return null;
            }
            var mouth = at ?? transform.TransformPoint(Vector3.up * (StartingFuelBody.Bounds(Kind).extents.y + 0.12f));
            var piece = FireStartingItems.Spawn(FireStartingItems.Content(Kind), mouth, transform.rotation);
            if (piece == null) return null;
            Count--;
            return piece;
        }

        private void Update()
        {
            if (Consumed) return;
            if (Kind == StartingFuelKind.Rag)
            {
                if (!Net.IsClient)
                {
                    if (LevelInfo.IsUnderWater(transform.position)) Rag.Extinguish();
                    Rag.Burn(Time.deltaTime);
                }
                // Oil soaks it dark and glossy; a burning rag scorches.
                var oiled = Rag.Oil / RagFuel.OilCapacity;
                var shown = (Rag.Lit ? 1 : 0) + oiled * 2f;
                if (shown != lastShown)
                {
                    lastShown = shown;
                    StartingFuelBody.SetState(renderers, appearance, Rag.Lit ? 0.25f : 0f, 0f, Rag.Lit ? 0.6f : 0f, oiled, seed, own: true);
                }
                if (flameLight != null)
                {
                    flameLight.gameObject.SetActive(Rag.Lit);
                    flameLight.intensity = 0.9f + 0.25f * Mathf.Sin(Time.time * 19f) * Mathf.Sin(Time.time * 7.3f);
                }
            }
            else if (reach != null && reach.gameObject.layer != gameObject.layer)
            {
                // The game layers an item's own colliders (World_Item, or
                // Grabbed_Item in hand); the contents must follow, or the
                // grabber's pointer passes through them.
                reach.gameObject.layer = gameObject.layer;
            }
            if (contents != null && shownCount != Count)
            {
                shownCount = Count;
                // Empty, the whole crate is what a hand takes hold of.
                if (reach != null) reach.enabled = Count > 0;
                for (var i = 0; i < contents.Length; i++)
                    if (contents[i] != null) contents[i].gameObject.SetActive(i < Count);
                if (item != null && item.ItemRigidbody != null)
                    item.ItemRigidbody.mass = FireStartingItems.EmptyMass(Kind) + Count * FireStartingItems.ContentMass(Kind);
            }
            if (Kind == StartingFuelKind.Wood || Kind == StartingFuelKind.Rag)
                FuelFireboxIntake.Track(this);
        }

        private Newtonsoft.Json.Linq.JObject Save(Newtonsoft.Json.Linq.JObject data)
        {
            data["rfFuelCount"] = Count; data["rfRagOil"] = Rag.Oil; data["rfRagLit"] = Rag.Lit;
            data["rfSupplyId"] = SupplyId;
            return data;
        }
        private void Restore(Newtonsoft.Json.Linq.JObject data)
        {
            var count = data["rfFuelCount"];
            Count = count?.Type == Newtonsoft.Json.Linq.JTokenType.Integer
                ? (int)Math.Max(0L, Math.Min(FireStartingItems.ContainerCapacity, (long)count)) : FireStartingItems.ContainerCapacity;
            var oil = data["rfRagOil"];
            var oilValue = oil?.Type == Newtonsoft.Json.Linq.JTokenType.Float || oil?.Type == Newtonsoft.Json.Linq.JTokenType.Integer ? (float)oil : 0f;
            Rag.Restore(oilValue, data["rfRagLit"]?.Type == Newtonsoft.Json.Linq.JTokenType.Boolean && (bool)data["rfRagLit"]);
            SupplyId = data["rfSupplyId"]?.Type == Newtonsoft.Json.Linq.JTokenType.String ? (string)data["rfSupplyId"] : null;
        }
        public string GetNameParam() => FireStartingItems.Name(Kind);
        public string GetCustomDescription() => Kind == StartingFuelKind.Wood
                ? "Split firewood. Throw it through the open fire door, or aim at the door and use it to lay it on the grate. Light it with a burning oiled rag."
            : Kind == StartingFuelKind.Rag ? (Rag.Lit ? $"Burning lighting rag, about {Rag.RemainingSeconds:F0} s of oil left. Throw it onto the wood, or into a prepared oil burner."
                : Rag.Oil > 0 ? "Oiled rag. Light it with your lighter, then throw it onto the wood." : "Dry lighting rag. Use the oil can on it, then your lighter.")
            : $"Free starting supplies: {Count} left. Reach in to take one, or use it while carrying it. Refills at its service-track supply.";
    }
}
