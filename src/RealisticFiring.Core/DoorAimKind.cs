using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// What a look through the fire door aims: the hand's tool decides what
    /// the aim may steer. Every kind lights the aimed zone (the label, the
    /// keypad, the bed, the overlay); only a shovel's aim places coal.
    /// </summary>
    public enum DoorAimKind
    {
        /// <summary>A loaded shovel's throw, or a lump of coal thrown in VR.</summary>
        Shovel,

        /// <summary>An empty shovel's rake, or the fire iron's.</summary>
        Rake,

        /// <summary>A piece of split firewood laid through the door by hand.</summary>
        LayWood,

        /// <summary>A lighting rag laid through the door by hand.</summary>
        LayRag,
    }

    /// <summary>What each kind of door aim steers, and how it is named.</summary>
    public static class DoorAimRules
    {
        /// <summary>
        /// Whether coal arriving now (a shovelful, the keyboard's shovel, a
        /// stoker's) lands where this aim points: only a shovel's, no older
        /// than <paramref name="hold"/> s. Wood or a rag being laid, or a
        /// rake, never steers coal: a keyboard shovelful or a stoker's coal
        /// just after would otherwise land on the wood's zone.
        /// </summary>
        public static bool AimsCoal(DoorAimKind kind, float age, float hold) => kind == DoorAimKind.Shovel && Fresh(age, hold);

        /// <summary>Whether an empty shovel's thrust rakes where this aim points: only a rake's, no older than <paramref name="hold"/> s.</summary>
        public static bool AimsRake(DoorAimKind kind, float age, float hold) => kind == DoorAimKind.Rake && Fresh(age, hold);

        /// <summary>Whether wood or a rag being laid goes where this aim points: one of its own kind, no older than <paramref name="hold"/> s.</summary>
        public static bool AimsLay(DoorAimKind aim, DoorAimKind laying, float age, float hold) =>
            Lays(laying) && aim == laying && Fresh(age, hold);

        /// <summary>Wood or a rag laid by hand.</summary>
        public static bool Lays(DoorAimKind kind) => kind == DoorAimKind.LayWood || kind == DoorAimKind.LayRag;

        /// <summary>
        /// Whether the aimed-zone label waits for the door to be open as far
        /// as the use needs: a rake or a lay does nothing through a door
        /// barely open. The shovel's label shows as it always has.
        /// </summary>
        public static bool LabelNeedsOpenDoor(DoorAimKind kind) => kind != DoorAimKind.Shovel;

        /// <summary>The aimed-zone label's name for what is aimed: "Shovel", "Rake", "Wood", "Rag".</summary>
        public static string Label(DoorAimKind kind)
        {
            switch (kind)
            {
                case DoorAimKind.Shovel: return "Shovel";
                case DoorAimKind.Rake: return "Rake";
                case DoorAimKind.LayWood: return "Wood";
                case DoorAimKind.LayRag: return "Rag";
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private static bool Fresh(float age, float hold) => age >= 0f && age <= hold;
    }
}
