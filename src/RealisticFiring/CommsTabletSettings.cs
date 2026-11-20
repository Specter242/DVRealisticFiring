namespace RealisticFiring
{
 public static class CommsTabletSettings
    {
        /// <summary>The buttons, in order: the same four as Unity Mod Manager's Controls → Equipment.</summary>
        public static string[] Actions() => EquipmentActions.Labels;

        /// <summary>A line under each of <see cref="Actions"/>.</summary>
        public static string[] ActionHelp() => EquipmentActions.Help;

        /// <summary>
        /// Gives action <paramref name="index"/>'s item at once, as its own
        /// button does (the multiplayer guest path included), in front of the
        /// player and never in their hand, which usually holds the tablet; shows
        /// its notice under the crosshair and returns it for the tablet's status
        /// strip; null for no such action.
        /// </summary>
        public static string Act(int index)
        {
            var notice = EquipmentActions.Run(index);
            if (!string.IsNullOrEmpty(notice))
                FireController.Announce(notice);
            return notice;
        }
    }
}
