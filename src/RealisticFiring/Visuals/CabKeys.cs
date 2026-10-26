using System.Linq;
using System.Reflection;
using DV.CabControls;
using DV.HUD;
using DV.KeyboardInput;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Keys for the mod's own cab controls: a control answers one of the
    /// game's incremental key pairs (its Gear B keys, say) as the game's own
    /// scrolled controls answer theirs, a notch a press, unless a control in
    /// the cab already answers it. The cab's keyboard control ticks the
    /// inputs it was built with; the new one joins them, and leaves with its
    /// control.
    /// </summary>
    internal static class CabKeys
    {
        /// <summary>
        /// Binds <paramref name="control"/> (a scrolled cab control) to the
        /// game's <paramref name="action"/> key pair, reversed with
        /// <paramref name="flip"/>; returns a note for the log.
        /// </summary>
        public static string Bind(Transform interior, Transform control, string action, bool flip)
        {
            var keys = interior.GetComponent<InteractablesKeyboardControl>() ?? interior.GetComponentInChildren<InteractablesKeyboardControl>(true);
            if (keys == null)
                return $"no {action} keys (the cab has no keyboard control)";
            var entries = keys.entries ?? new AKeyboardInput[0];
            var taken = entries.FirstOrDefault(k => k != null && Answers(k, action));
            if (taken != null)
                return $"no {action} keys (they turn the cab's '{taken.name}')";
            var input = control.gameObject.AddComponent<MouseScrollKeyboardInput>();
            input.scrollAction = new AKeyboardInput.ActionReference { name = action, flip = flip };
            keys.entries = entries.Append(input).ToArray();
            control.gameObject.AddComponent<Leaving>().Set(keys, input);
            var manager = keys.GetComponent<InteriorControlsManager>();
            if (manager == null)
                input.SetupActions(null);
            else if (manager.Initialized)
                input.SetupActions(manager);
            else
                manager.OnInitialized += m =>
                {
                    if (input != null)
                        input.SetupActions(m);
                };
            return $"on the {action} keys";
        }

        // Whether a keyboard input answers the action, in any of its action fields.
        private static bool Answers(AKeyboardInput input, string action) =>
            input.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(f => f.FieldType == typeof(AKeyboardInput.ActionReference))
                .Select(f => f.GetValue(input) as AKeyboardInput.ActionReference)
                .Any(a => a != null && a.name == action);

        // Takes the input off the cab's list when its control goes, so the
        // cab's keyboard control never ticks a destroyed one.
        private sealed class Leaving : MonoBehaviour
        {
            private InteractablesKeyboardControl keys;
            private AKeyboardInput input;

            public void Set(InteractablesKeyboardControl keyboard, AKeyboardInput added)
            {
                keys = keyboard;
                input = added;
            }

            private void OnDestroy()
            {
                if (keys != null && keys.entries != null)
                    keys.entries = keys.entries.Where(k => k != null && k != input).ToArray();
            }
        }
    }
}
