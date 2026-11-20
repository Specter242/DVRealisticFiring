using System;
using HarmonyLib;

namespace RealisticFiring.Multiplayer
{
    /// <summary>
    /// Whether the Multiplayer mod shares world items between the players'
    /// games. Kept apart from the Multiplayer mod's API types, so the checks
    /// run it against the installed Multiplayer without a session.
    /// </summary>
    internal static class ItemSharing
    {
        /// <summary>
        /// Whether the Multiplayer mod applies the item changes it receives
        /// (<see cref="Net.ItemsRemote"/>). Its 0.1.16 doesn't: the handlers
        /// for its item packet, on the host and on a guest, are empty, so a
        /// crate the host set out never reached a guest (Ace, DV Discord,
        /// 2026-11-19). <paramref name="why"/> says how it was told.
        /// </summary>
        internal static bool Shared(out string why)
        {
            try
            {
                var client = AccessTools.TypeByName("Multiplayer.Networking.Managers.Client.NetworkClient");
                var handler = client != null ? AccessTools.Method(client, "OnCommonItemChangePacket") : null;
                var body = handler?.GetMethodBody()?.GetILAsByteArray();
                if (body == null)
                {
                    why = "no item packet handler found";
                    return false;
                }
                // An empty method is a lone ret, or nop and ret.
                why = body.Length <= 2 ? "its item packet handler is empty" : "its item packet handler works";
                return body.Length > 2;
            }
            catch (Exception e)
            {
                why = $"the item packet handler could not be read ({e.GetType().Name})";
                return false;
            }
        }
    }
}
