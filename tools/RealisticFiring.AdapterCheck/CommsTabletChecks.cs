using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DV.Items;
using HarmonyLib;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
 internal static class CommsTabletChecks
    {
        // The Comms Tablet's delegate for each member (its Guests/GuestRegistry.cs, contract 1).
        private static readonly (string name, Type type, bool required)[] Members =
        {
            ("Icon", typeof(Func<Texture2D>), true),
            ("Available", typeof(Func<bool>), true),
            ("Open", typeof(Func<RectTransform, GameObject, Action, Action<int, Rect, bool>, Action<int>, int, bool, int>), true),
            ("Close", typeof(Action<int>), true),
            ("Shown", typeof(Action<int, bool>), true),
            ("Held", typeof(Action<int, bool>), true),
            ("Update", typeof(Action<int>), true),
            ("Draw", typeof(Action<int>), true),
            ("Pressed", typeof(Action<int, int>), true),
            ("PressArea", typeof(Func<int, Rect>), true),
            ("PressAt", typeof(Action<int, Vector2>), true),
            ("Use", typeof(Func<int, bool>), false),
            ("Scroll", typeof(Func<int, bool, bool>), false),
        };

        internal static void Run(Action<bool, string> check)
        {
            var type = typeof(CommsTabletApp);
            var assembly = type.Assembly.GetName().Name;
            check(type.IsPublic && type.IsAbstract && type.IsSealed && type.FullName == assembly + ".CommsTabletApp",
                "the Comms Tablet guest app is a public static class named for its assembly (RealisticFiring.CommsTabletApp)");
            object Constant(string name) => type.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue();
            check(Constant("Contract") is int contract && contract == 1 && Constant("Id") as string == "realistic-firing"
                  && Constant("Name") as string == "Firing" && Constant("Tablet") as string == Items.FiringTablet.PrefabName,
                "it declares contract 1, its id, the app's name Firing, and the firing tablet's prefab name to retire");
            var unbound = new List<string>();
            foreach (var (name, delegateType, required) in Members)
            {
                var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == name).ToList();
                if (methods.Count == 0 ? required : !methods.Any(m => Delegate.CreateDelegate(delegateType, m, false) != null))
                    unbound.Add(name);
            }
            check(unbound.Count == 0, "every member binds to the Comms Tablet's delegate types" + (unbound.Count > 0 ? ": not " + string.Join(", ", unbound) : ""));
            var settingsType = type.Assembly.GetType(assembly + ".CommsTabletSettings");
            MethodInfo Member(string name) => settingsType?.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            var bound = settingsType != null && settingsType.IsPublic && settingsType.IsAbstract && settingsType.IsSealed
                        && Delegate.CreateDelegate(typeof(Func<string[]>), Member("Actions"), false) != null
                        && Delegate.CreateDelegate(typeof(Func<int, string>), Member("Act"), false) != null
                        && Delegate.CreateDelegate(typeof(Func<string[]>), Member("ActionHelp"), false) != null
                        && settingsType.GetField("Contract") == null;
            check(bound, "the Settings app's buttons bind as the Comms Tablet binds them: RealisticFiring.CommsTabletSettings, public and static, Actions and Act a pair, ActionHelp, no contract constant");
            var actions = CommsTabletSettings.Actions();
            var help = CommsTabletSettings.ActionHelp();
            check(actions.SequenceEqual(new[] { "Give fire iron", "Give firewood crate", "Give rag tin", "Give firing manual" })
                  && help.Length == actions.Length && help.All(h => !string.IsNullOrEmpty(h)) && CommsTabletSettings.Act(-1) == null && CommsTabletSettings.Act(actions.Length) == null,
                $"the tablet's Settings app gets the four give actions, each with a line of help, and an action out of range does nothing ({string.Join(", ", actions)})");
            check(type.GetMethod("Actions") == null && type.GetMethod("Act") == null, "the guest app itself has no action members: they are the settings class's");
            var referenced = type.Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
            check(!referenced.Contains(CommsTabletLink.HostAssembly), "the mod never references the Comms Tablet's assembly; they meet by reflection only");

            // The panel's buttons at the Comms Tablet's 1080 by 924: inside the screen and apart.
            var areas = Enumerable.Range(0, Items.FiringTabletPanel.Buttons).Select(i => Scaled(Items.FiringTabletPanel.ButtonArea(i), 2f)).ToList();
            var screen = new Rect(0, 0, 1080, 924);
            check(areas.All(a => a.xMin >= screen.xMin && a.yMin >= screen.yMin && a.xMax <= screen.xMax && a.yMax <= screen.yMax)
                  && areas.SelectMany((a, i) => areas.Skip(i + 1), (a, b) => !a.Overlaps(b)).All(apart => apart)
                  && Items.FiringTabletPanel.Buttons == Items.FiringTabletGadget.Pages + 6,
                "doubled for the Comms Tablet's screen, the four page tabs and six orders fit inside it and none overlap");

            // The link: false with no Comms Tablet; with one, whatever it answers for this id.
            check(!CommsTabletLink.Merged, "without the Comms Tablet the firing tablet is not retired");
            var answer = EmitHost();
            check(!CommsTabletLink.Merged, "with a Comms Tablet that has not bound the app, the firing tablet is not retired");
            answer.SetValue(null, true);
            check(CommsTabletLink.Merged, "with a Comms Tablet that has bound it, the link finds its GuestContract by name and says merged");
            check(CommsTabletLink.RetiredNotice.Contains("Firing app") && CommsTabletLink.RetiredNotice.Contains("Comms Tablet"),
                "the settings' give button says where the pages are instead: " + CommsTabletLink.RetiredNotice);

            // Merged, the steam kit gives no firing tablet and its key is cleared; unmerged, it does as before.
            var prefabName = AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName");
            InventoryItemSpec Spec(string name)
            {
                var spec = (InventoryItemSpec)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(InventoryItemSpec));
                prefabName.SetValue(spec, name);
                return spec;
            }
            var steam = new List<StartingItems.StartingItem>
            {
                new StartingItems.StartingItem { item = Spec("Radio") },
                new StartingItems.StartingItem { item = Spec("shovel"), preferredRelativeSlot = 3 },
            };
            var tablet = Spec(Items.FiringTablet.PrefabName);
            // A save that had the firing tablet before the Comms Tablet came: merged, its key is cleared
            // (that tablet loads as the Comms Tablet's own), and once the Comms Tablet is gone the kit
            // gives the firing tablet once again.
            var given = new SaveGameData();
            given.SetBool(Items.FiringTabletStartingItems.GivenKey, true);
            var givenBuilt = 0;
            var givenKit = Items.FiringTabletStartingItems.WithTabletOnce(given, steam, () => { givenBuilt++; return tablet; });
            check(ReferenceEquals(givenKit, steam) && givenBuilt == 0 && given.GetBool(Items.FiringTabletStartingItems.GivenKey) == null,
                "merged, a save that had the firing tablet keeps its steam kit as it is and its given key is cleared");
            answer.SetValue(null, false);
            givenKit = Items.FiringTabletStartingItems.WithTabletOnce(given, steam, () => { givenBuilt++; return tablet; });
            check(givenKit.Count == 3 && givenBuilt == 1 && given.GetBool(Items.FiringTabletStartingItems.GivenKey) == true,
                "with the Comms Tablet removed, that save's steam kit gives the firing tablet once again and marks it given");
            answer.SetValue(null, true);

            var data = new SaveGameData();
            var built = 0;
            var kit = Items.FiringTabletStartingItems.WithTabletOnce(data, steam, () => { built++; return tablet; });
            check(ReferenceEquals(kit, steam) && built == 0 && data.GetBool(Items.FiringTabletStartingItems.GivenKey) == null,
                "merged, the steam kit is left as it is (the Comms Tablet gives its own) and the firing tablet's given key cleared");
            answer.SetValue(null, false);
            kit = Items.FiringTabletStartingItems.WithTabletOnce(data, steam, () => { built++; return tablet; });
            check(kit.Count == 3 && built == 1 && data.GetBool(Items.FiringTabletStartingItems.GivenKey) == true,
                "with the Comms Tablet removed, that save's steam kit gives the firing tablet once, as a new player's");
        }

        /// <summary>The Comms Tablet's own binder, from its assembly: it binds this app with nothing left out.</summary>
        internal static void CheckWithHost(string path, Action<bool, string> check)
        {
            Assembly host;
            try
            {
                // From its bytes: each build named is its own, even beside another Comms Tablet already loaded.
                host = Assembly.Load(System.IO.File.ReadAllBytes(path));
            }
            catch (Exception e)
            {
                check(false, "the Comms Tablet's assembly loads: " + e.Message);
                return;
            }
            // Its Settings app's reader, on these settings.
            TabletSettingsChecks.CheckWithHost(host, check);
            var contract = host.GetType(CommsTabletLink.ContractType, false);
            if (contract == null)
            {
                // A Comms Tablet from before the guest contract (its eighth slice): Realistic Firing stays on its own.
                Console.WriteLine($"skip the Comms Tablet at {path} has no guest contract (older than its eighth slice); name a newer build to check the binding");
                return;
            }
            var min = contract?.GetField("Min")?.GetRawConstantValue() as int?;
            var max = contract?.GetField("Max")?.GetRawConstantValue() as int?;
            check(min <= CommsTabletApp.Contract && CommsTabletApp.Contract <= max,
                $"the Comms Tablet named takes guest contract {CommsTabletApp.Contract} (it takes {min} to {max})");
            var bind = host.GetType("CommsTablet.Guests.GuestApp", false)?.GetMethod("Bind", BindingFlags.NonPublic | BindingFlags.Static);
            if (bind == null)
            {
                check(false, "the Comms Tablet's guest binder resolves");
                return;
            }
            var args = new object[] { typeof(CommsTabletApp), typeof(CommsTabletApp).Assembly.GetName().Name, null };
            var bound = bind.Invoke(null, args);
            check(bound != null && args[2] == null, "the Comms Tablet's own binder binds Realistic Firing's app" + (args[2] != null ? ": " + args[2] : ""));
            var tablet = bound?.GetType().GetField("Tablet", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(bound) as string;
            check(tablet == Items.FiringTablet.PrefabName, "and retires the firing tablet by its prefab name, " + tablet);
        }

        private static Rect Scaled(Rect r, float scale) => new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale);

        /// <summary>
        /// A stand-in Comms Tablet: an assembly named CommsTablet whose
        /// CommsTablet.GuestContract.Merged(id) is true for this mod's id while
        /// its Answer field is; returns that field.
        /// </summary>
        private static FieldInfo EmitHost()
        {
            var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(CommsTabletLink.HostAssembly), AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule(CommsTabletLink.HostAssembly);
            var type = module.DefineType(CommsTabletLink.ContractType, TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);
            var answer = type.DefineField("Answer", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
            var merged = type.DefineMethod("Merged", MethodAttributes.Public | MethodAttributes.Static, typeof(bool), new[] { typeof(string) });
            var il = merged.GetILGenerator();
            var no = il.DefineLabel();
            il.Emit(OpCodes.Ldsfld, answer);
            il.Emit(OpCodes.Brfalse_S, no);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, CommsTabletApp.Id);
            il.Emit(OpCodes.Call, typeof(string).GetMethod("op_Equality", new[] { typeof(string), typeof(string) }));
            il.Emit(OpCodes.Ret);
            il.MarkLabel(no);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ret);
            return type.CreateType().GetField("Answer");
        }
    }
}
