namespace RealisticFiring
{
 internal static class EquipmentActions
    {
        internal const int FireIron = 0, WoodCrate = 1, RagTin = 2, Manual = 3, Checklists = 4;

        internal static readonly string[] Labels = { "Give fire iron", "Give firewood crate", "Give rag tin", "Give firing manual", "Give firing checklists" };

        internal static readonly string[] Help =
        {
            "Through the open door: click to slice, scroll to push or pull, Shift + scroll to move sideways.",
            "A full crate of firewood for lighting a cold fire, when none is at hand.",
            "A full tin of rags to oil and light, when none is at hand.",
            "An illustrated booklet with a clickable table of contents.",
            "A paper booklet of the six firing checklists, one a page.",
        };

        /// <summary>Gives the item of <paramref name="action"/>; its notice, or null for no such action.</summary>
        internal static string Run(int action)
        {
            switch (action)
            {
                case FireIron:
                    // Where the Multiplayer mod shares items only the host makes them; the shops sell the iron.
                    return Net.ItemsRemote ? "In multiplayer, buy the fire iron at a shop, beside the shovel" : Items.FireIron.GiveToPlayer();
                case WoodCrate:
                    return Net.ItemsRemote ? "In multiplayer, only the host can give a firewood crate" : Items.FireStartingItems.GiveToPlayer(Items.StartingFuelKind.WoodCrate);
                case RagTin:
                    return Net.ItemsRemote ? "In multiplayer, only the host can give a rag tin" : Items.FireStartingItems.GiveToPlayer(Items.StartingFuelKind.RagBox);
                case Manual:
                    return Net.ItemsRemote ? "Ask the multiplayer host to give you a firing manual" : Items.FiringManual.GiveToPlayer();
                case Checklists:
                    return Net.ItemsRemote ? "In multiplayer, buy the firing checklists at a gadget shop, beside the clinometer" : Items.ChecklistBooklet.GiveToPlayer();
                default:
                    return null;
            }
        }
    }
}
