using MelonLoader;
using HarmonyLib;
using System.Reflection;
using System.Collections;
using SoL.Game;
using SoL.Game.UI;
using SoL.Game.Objects.Containers;
using SoL.Game.Objects.Archetypes;
using System.Runtime.CompilerServices;

namespace AutoLootMod
{
    public class AutoLootMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll(typeof(AutoLootPatch_ContainerInstance));
            MelonLogger.Msg("AutoLootMod initialized and Harmony patched (ContainerInstance events).");
        }
    }

    [HarmonyPatch(typeof(ContainerInstance), "Add")]
    public static class AutoLootPatch_ContainerInstance
    {
        static void Postfix(ContainerInstance __instance, ArchetypeInstance instance, bool update)
        {
            if (__instance.ContainerType != ContainerType.Loot)
                return;

            var player = LocalPlayer.GameEntity;
            var playerInventory = player?.CollectionController?.Inventory;
            if (playerInventory == null || __instance == playerInventory)
                return;

            if (__instance.Count > 0)
            {
                if (!HasLooted(__instance))
                {
                    MarkAsLooted(__instance);
                    MelonLogger.Msg("[AutoLootMod] Auto-looting items from loot container!");
                    var response = __instance.MoveContentsToContainerInstance(playerInventory, true, false);
                    MelonLogger.Msg($"[AutoLootMod] Looted {response.Items?.Length ?? 0} items, currency: {response.Currency}");
                }
            }
        }

        private static ConditionalWeakTable<ContainerInstance, object> _lootedContainers = new ConditionalWeakTable<ContainerInstance, object>();
        private static bool HasLooted(ContainerInstance container) => _lootedContainers.TryGetValue(container, out _);
        private static void MarkAsLooted(ContainerInstance container) => _lootedContainers.GetValue(container, _ => new object());
    }
}