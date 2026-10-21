using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DV.CabControls;
using DV.Shops;
using DV.Utils;
using HarmonyLib;
using RealisticFiring.Visuals;
using DV;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The fire iron: a fireman's slice bar, a new hand-held item made from
    /// the stock shovel at runtime. A copy of the shovel prefab is kept
    /// dormant under an inactive holder (so nothing on it wakes), stripped of
    /// the shovel's own behaviour, scoop, and coal visuals, given the iron's
    /// mesh, look, colliders, and <see cref="FireIronUse"/>, and named so the
    /// game saves it; <see cref="FireIronLoading"/> hands it back when a
    /// save or shop asks for that name. Without the mod a saved iron is
    /// skipped and dropped, nothing breaks.
    /// </summary>
    internal static class FireIron
    {
        public const string PrefabName = "RealisticFiring_FireIron";
        public const string DisplayName = "Fire iron (slice bar)";
        private const string SourcePrefab = "shovel";
        private const float Mass = 4.5f;

        // The shovel's own behaviour and its coal-carrying parts. Its
        // ShopRestocker stays: the shops count bought irons by it, and give
        // the stock back when one is destroyed. Its [ShovelMountAnchor]
        // stays too: the shovel mount gadget is a sliding snap point, which
        // takes only an item whose anchor for it slides
        // (SnapPointGadgetSliding.CanSnapCheck), and the shovel's sits at its
        // origin on the shaft, where the iron's bar runs too. Without it the
        // iron would not hang on the mount (SmoothJazz17, Nexus, 2026-10-19).
        private static readonly string[] ShovelComponents = { "ShovelAudio", "Shovel", "ShovelNonPhysicalCoal" };
        private static readonly string[] ShovelChildren = { "scoop pivot", "[spawn anchors]", "[visual coal anchor]" };

        private static GameObject template;
        private static bool noShovel;

        /// <summary>
        /// The dormant template, built on first use and again if something
        /// destroyed it (a player, 2026-10-15: a bought iron came out null);
        /// null if the shovel could not be found.
        /// </summary>
        public static GameObject Template
        {
            get
            {
                if (template == null && !noShovel)
                {
                    if (!ReferenceEquals(template, null))
                        Main.Mod.Logger.Warning("The fire iron's template was destroyed; making it again");
                    template = Build();
                    noShovel = template == null;
                }
                return template;
            }
        }

        /// <summary>
        /// Lists the fire iron among the game's items, where the Multiplayer
        /// mod looks up an item's prefab by name to make it on clients. Only
        /// done in a multiplayer session; returns whether it is listed.
        /// </summary>
        public static bool RegisterAsGameItem()
        {
            var spec = Template != null ? Template.GetComponent<InventoryItemSpec>() : null;
            var items = Globals.G != null && Globals.G.Items != null ? Globals.G.Items.items : null;
            if (spec == null || items == null)
                return false;
            if (!items.Contains(spec))
                items.Add(spec);
            return true;
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
            // The shops read the template's name for the shelf tag and receipt.
            spec.NameOverride = DisplayName;
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

        /// <summary>
        /// A bought iron put on <paramref name="shop"/>'s counter, as the
        /// game hands over what it sells, for a purchase the game could not
        /// make; false without the template or the counter.
        /// </summary>
        public static bool HandOver(Shop shop)
        {
            var source = Template;
            if (source == null || shop == null || shop.itemSpawnTransform == null)
                return false;
            var iron = Object.Instantiate(source, shop.itemSpawnTransform.position, Quaternion.identity);
            iron.name = PrefabName;
            iron.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
            var restocker = iron.GetComponent<ShopRestocker>();
            if (restocker != null)
                restocker.restockOnItemDestroyed = true;
            var item = iron.GetComponent<ItemBase>();
            if (item == null)
            {
                Object.Destroy(iron);
                return false;
            }
            SingletonBehaviour<StorageController>.Instance.AddItemToWorldStorage(item);
            if (item.ItemRigidbody != null)
            {
                item.ItemRigidbody.velocity = Vector3.zero;
                item.ItemRigidbody.angularVelocity = Vector3.zero;
            }
            return true;
        }
    }

    /// <summary>
    /// Resolve the fire iron only at the game's saved-item and purchased-item
    /// load sites. Patching the small Unity Resources.Load wrapper globally
    /// can break unrelated resource loads during startup on the game's Mono.
    /// Keep the rest of item instantiation, accounting, and restoration stock.
    /// </summary>
    [HarmonyPatch]
    internal static class FireIronLoading
    {
        internal static MethodInfo ResourceLoad =>
            AccessTools.GetDeclaredMethods(typeof(Resources)).First(m =>
                m.Name == nameof(Resources.Load) && !m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(StartingItemsController), "InstantiateItem");
            yield return AccessTools.EnumeratorMoveNext(
                AccessTools.DeclaredMethod(typeof(DV.Shops.GlobalShopController), "InstantiatePurchasedItems"));
        }

        internal static Object LoadPrefab(string path) =>
            path == FireIron.PrefabName ? FireIron.Template : Resources.Load(path);

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            Redirect(instructions, original, SkinManagerFireIronLoading.Found ? "Skin Manager's loader" : null);

        /// <summary>
        /// Points each prefab load in <paramref name="instructions"/> at
        /// <see cref="LoadPrefab"/>. Where there is none, says so: as a
        /// note when <paramref name="coveredBy"/> makes the items there
        /// instead (and its load is redirected), else as a warning naming the
        /// other mods patching the method.
        /// </summary>
        internal static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions, MethodBase original, string coveredBy)
        {
            var load = ResourceLoad;
            var replacement = AccessTools.DeclaredMethod(typeof(FireIronLoading), nameof(LoadPrefab));
            var redirected = 0;
            foreach (var instruction in instructions)
            {
                if (!instruction.Calls(load))
                {
                    yield return instruction;
                    continue;
                }
                redirected++;
                yield return new CodeInstruction(instruction) { opcode = OpCodes.Call, operand = replacement };
            }
            if (redirected > 0)
                yield break;
            var where = $"{original?.DeclaringType?.Name}.{original?.Name}";
            if (coveredBy != null)
            {
                Main.Mod?.Logger.Log($"The fire iron's lookup is in {coveredBy}, which makes the items in {where}");
                yield break;
            }
            // Another mod's patch there, or a game update, can take the load
            // away: the shops then sell the iron but cannot make it.
            var others = original == null ? new string[0]
                : Harmony.GetPatchInfo(original)?.Owners.Where(o => o != Main.Mod?.Info.Id).ToArray() ?? new string[0];
            var effect = original?.DeclaringType == typeof(StartingItemsController) ? "a saved iron is dropped as the save loads"
                : original?.DeclaringType?.Assembly == typeof(StartingItemsController).Assembly ? "a bought iron is handed over by the purchase guard"
                : "the iron is not found there";
            Main.Mod?.Logger.Warning($"The fire iron's lookup found no prefab load in {where}: {effect}" +
                                     (others.Length > 0 ? $"; also patched by {string.Join(", ", others)}" : ""));
        }
    }

    /// <summary>
    /// Skin Manager (SkinManagerMod; 4.4.1 read 2026-10-17) rewrites both
    /// game methods <see cref="FireIronLoading"/> patches, making the items
    /// through its own helpers, which load the prefab by name themselves:
    /// with it installed, a bought iron fell to the purchase guard and a
    /// saved one was dropped on every load (found from the lookup's warning
    /// in DaGriffon's log, 2026-10-17).
    /// The iron's lookup is redirected in those helpers too. Its own class,
    /// so that a change in Skin Manager costs only this; loaded after Skin
    /// Manager (Info.json's LoadAfter), so its assembly is there to find.
    /// </summary>
    [HarmonyPatch]
    internal static class SkinManagerFireIronLoading
    {
        internal const string AssemblyName = "SkinManagerMod";

        internal static readonly (string Type, string Method)[] Loaders =
        {
            ("SkinManagerMod.Patches.ShopPatches", "InstantiatePaintCan"),
            ("SkinManagerMod.Patches.InventoryPatches", "CustomInstantiateItem"),
        };

        /// <summary>Skin Manager's item loaders, those it has; none without it.</summary>
        internal static List<MethodBase> Targets()
        {
            var assembly = System.AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == AssemblyName);
            if (assembly == null)
                return new List<MethodBase>();
            return Loaders.Select(l => assembly.GetType(l.Type, false))
                .Select((type, i) => type == null ? null : (MethodBase)AccessTools.DeclaredMethod(type, Loaders[i].Method))
                .Where(m => m != null).ToList();
        }

        /// <summary>Whether Skin Manager's loaders are all there, so the game methods it rewrites are covered.</summary>
        internal static bool Found => Targets().Count == Loaders.Length;

        internal static bool Prepare() => Targets().Count > 0;

        internal static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = Targets();
            if (targets.Count < Loaders.Length)
                Main.Mod?.Logger.Warning("Skin Manager's item loaders have changed: the fire iron may not be found in its shops or saves");
            return targets;
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
            FireIronLoading.Redirect(instructions, original, null);
    }
}
