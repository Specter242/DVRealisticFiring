using System.Linq;
using DV.CabControls;
using DV.Utils;
using HarmonyLib;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The fire iron: a fireman's slice bar, a new hand-held item made from
    /// the stock shovel at runtime. A copy of the shovel prefab is kept
    /// dormant under an inactive holder (so nothing on it wakes), stripped of
    /// the shovel's own behaviour, scoop, and coal visuals, given the iron's
    /// mesh, look, colliders, and <see cref="FireIronUse"/>, and named so the
    /// game saves it; <see cref="ResourcesLoadPatch"/> hands it back when a
    /// save or shop asks for that name. Without the mod a saved iron is
    /// skipped and dropped, nothing breaks.
    /// </summary>
    internal static class FireIron
    {
        public const string PrefabName = "RealisticFiring_FireIron";
        public const string DisplayName = "Fire iron (slice bar)";
        private const string SourcePrefab = "shovel";
        private const float Mass = 4.5f;

        // The shovel's own behaviour and its coal-carrying parts.
        private static readonly string[] ShovelComponents = { "ShovelAudio", "Shovel", "ShopRestocker", "ShovelNonPhysicalCoal" };
        private static readonly string[] ShovelChildren = { "scoop pivot", "[spawn anchors]", "[visual coal anchor]", "[ShovelMountAnchor]" };

        private static GameObject template;
        private static bool tried;

        /// <summary>The dormant template, built on first use; null if the shovel could not be found.</summary>
        public static GameObject Template
        {
            get
            {
                if (template == null && !tried)
                {
                    tried = true;
                    template = Build();
                }
                return template;
            }
        }

        private static GameObject Build()
        {
            var shovel = Resources.Load<GameObject>(SourcePrefab);
            if (shovel == null)
            {
                Main.Mod.Logger.Warning($"No '{SourcePrefab}' prefab to make the fire iron from");
                return null;
            }
            var holder = new GameObject("[RealisticFiring item templates]");
            holder.SetActive(false);
            Object.DontDestroyOnLoad(holder);
            var iron = Object.Instantiate(shovel, holder.transform, false);
            iron.name = PrefabName;

            var spec = iron.GetComponent<InventoryItemSpec>();
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(spec, PrefabName);
            SetIcons(spec);
            foreach (var name in ShovelComponents)
            {
                foreach (var component in iron.GetComponents<MonoBehaviour>().Where(c => c != null && c.GetType().Name == name).ToList())
                    Object.DestroyImmediate(component);
            }
            foreach (var name in ShovelChildren)
            {
                var child = iron.transform.Find(name);
                if (child != null)
                    Object.DestroyImmediate(child.gameObject);
            }

            // The iron's shape and look on the shovel's model.
            var model = iron.transform.Find("model");
            if (model != null)
            {
                foreach (var lod in model.GetComponents<LODGroup>().ToList())
                    Object.DestroyImmediate(lod);
                model.localPosition = Vector3.zero;
                model.localRotation = Quaternion.identity;
                model.localScale = Vector3.one;
                model.GetComponent<MeshFilter>().sharedMesh = FireIronMesh.Create();
                var renderer = model.GetComponent<MeshRenderer>();
                if (PlumeAssets.FireIronMaterial != null)
                    renderer.sharedMaterial = PlumeAssets.FireIronMaterial;
            }

            // Held by the bar and the ring.
            Reshape(iron.transform.Find("stick"), new Vector3(0f, 0f, (FireIronMesh.BarStart + FireIronMesh.Tip) / 2f),
                FireIronMesh.Tip - FireIronMesh.BarStart, 0.02f, 2);
            Reshape(iron.transform.Find("handle"), new Vector3(0f, 0f, FireIronMesh.RingCenter),
                2f * FireIronMesh.RingRadius + 0.03f, FireIronMesh.RingRadius, 1);
            var item = iron.GetComponent<DV.CabControls.Spec.Item>();
            if (item != null)
            {
                item.rigidbodyMass = Mass;
                item.colliderGameObjects = new[] { "stick", "handle" }
                    .Select(n => iron.transform.Find(n))
                    .Where(t => t != null)
                    .Select(t => t.gameObject)
                    .ToArray();
            }

            // Where the working animation puts the iron: its blade.
            var tip = new GameObject(FireIronUse.TipName).transform;
            tip.SetParent(iron.transform, false);
            tip.localPosition = new Vector3(0f, 0f, FireIronMesh.BladeStart);

            if (!VRManager.IsVREnabled() && iron.GetComponent<ItemScrollingNonVR>() == null)
                iron.AddComponent<ItemScrollingNonVR>();
            iron.AddComponent<FireIronUse>();

            Main.Mod.Logger.Log($"Fire iron made from the '{SourcePrefab}' prefab: components " +
                                string.Join(", ", iron.GetComponents<Component>().Select(c => c.GetType().Name)) +
                                (PlumeAssets.FireIronMaterial != null ? "" : "; no effects bundle, so it keeps the shovel's look"));
            return iron;
        }

        // The iron's own icons in place of the shovel's, when the bundle has them.
        private static void SetIcons(InventoryItemSpec spec)
        {
            var icon = Sprite(PlumeAssets.FireIronIcon);
            var dropped = Sprite(PlumeAssets.FireIronIconDropped);
            if (icon != null)
            {
                AccessTools.Field(typeof(InventoryItemSpec), "itemIconSprite").SetValue(spec, icon);
                AccessTools.Field(typeof(InventoryItemSpec), "itemIconSpriteStandard").SetValue(spec, icon);
            }
            if (dropped != null)
                AccessTools.Field(typeof(InventoryItemSpec), "itemIconSpriteDropped").SetValue(spec, dropped);
        }

        private static Sprite Sprite(Texture2D texture) =>
            texture == null ? null : UnityEngine.Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);

        private static void Reshape(Transform holder, Vector3 center, float height, float radius, int direction)
        {
            var capsule = holder != null ? holder.GetComponent<CapsuleCollider>() : null;
            if (capsule == null)
                return;
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            capsule.center = center;
            capsule.height = height;
            capsule.radius = radius;
            capsule.direction = direction;
        }

        /// <summary>Puts a new fire iron in front of the player, as the game's own spawn command does.</summary>
        public static string GiveToPlayer()
        {
            var source = Template;
            var camera = PlayerManager.ActiveCamera;
            if (source == null || camera == null)
                return "no fire iron: the shovel to make it from or the player's camera is missing";
            var view = camera.transform;
            var iron = Object.Instantiate(source, view.position + view.forward * 0.6f, view.rotation);
            iron.name = PrefabName;
            iron.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
            var item = iron.GetComponent<ItemBase>();
            if (item == null)
            {
                Object.Destroy(iron);
                return "no fire iron: the copy did not become an item";
            }
            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(item);
            return "a fire iron is in front of you";
        }
    }

    /// <summary>
    /// Saves and shops load items by prefab name through Resources.Load; the
    /// fire iron is not a resource, so hand back its template. The target is
    /// picked by hand: Load(string) and Load&lt;T&gt;(string) share their
    /// parameters, which Harmony's attribute lookup cannot tell apart.
    /// </summary>
    [HarmonyPatch]
    internal static class ResourcesLoadPatch
    {
        private static System.Reflection.MethodBase TargetMethod() =>
            AccessTools.GetDeclaredMethods(typeof(Resources)).First(m =>
                m.Name == nameof(Resources.Load) && !m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

        private static bool Prefix(string path, ref Object __result)
        {
            if (path != FireIron.PrefabName)
                return true;
            __result = FireIron.Template;
            return false;
        }
    }
}
