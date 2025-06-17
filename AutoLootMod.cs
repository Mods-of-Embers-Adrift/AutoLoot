using MelonLoader;
using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;
using SoL.Game;
using SoL.Game.UI;
using SoL.Game.Objects.Containers;
using SoL.Game.Objects.Archetypes;
using System.Runtime.CompilerServices;
using UnityEngine;
using System.Linq;
using System.Collections;

namespace AutoLootMod
{
    public class AutoLootMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            HarmonyInstance.PatchAll(typeof(AutoLootPatch_ContainerInstance));
        }
    }

    [HarmonyPatch(typeof(ContainerInstance), "Add")]
    public static class AutoLootPatch_ContainerInstance
    {
        private static ConditionalWeakTable<ContainerInstance, object> _lootedContainers = new ConditionalWeakTable<ContainerInstance, object>();

        static void Postfix(ContainerInstance __instance, ArchetypeInstance instance, bool update)
        {
            if (__instance.ContainerType != ContainerType.Loot)
                return;

            var player = LocalPlayer.GameEntity;
            var playerInventory = player?.CollectionController?.Inventory;
            if (playerInventory == null || __instance == playerInventory)
                return;

            if (__instance.Count > 0 && !HasLooted(__instance))
            {
                MarkAsLooted(__instance);

                if (HasNeedOrGreedItems(__instance))
                {
                    MelonLogger.Msg("[AutoLootMod] Skipping auto-loot: loot roll (Need/Greed) is pending on at least one item.");
                    return;
                }

                TryAutoLootWithRetry(__instance, playerInventory);
            }
        }

        private static void TryAutoLootWithRetry(ContainerInstance loot, ContainerInstance playerInventory)
        {
            loot.MoveContentsToContainerInstance(playerInventory, true, false);
            MelonCoroutines.Start(RetryLootCoroutine(loot, playerInventory));
        }

        private static IEnumerator RetryLootCoroutine(ContainerInstance loot, ContainerInstance playerInventory)
        {
            int maxTries = 20;
            int tries = 0;
            while (loot.Count > 0 && tries < maxTries)
            {
                yield return new WaitForSeconds(0.05f);
                loot.MoveContentsToContainerInstance(playerInventory, true, false);
                tries++;
            }
            if (loot.Count > 0)
            {
                MelonLogger.Msg($"[AutoLootMod] Warning: After {maxTries} auto-loot attempts, {loot.Count} items remain.");
            }
        }

        private static bool HasNeedOrGreedItems(ContainerInstance container)
        {
            var lootRollWindow = GameObject.FindObjectOfType<SoL.Game.UI.Loot.LootRollWindow>();
            if (lootRollWindow == null)
                return false;

            var lootRollItemsField = lootRollWindow.GetType().GetField("m_lootRollitems", BindingFlags.NonPublic | BindingFlags.Instance);
            var lootRollItems = lootRollItemsField.GetValue(lootRollWindow) as SoL.Game.UI.Loot.LootRollItemUI[];
            if (lootRollItems == null)
                return false;

            foreach (var slot in lootRollItems)
            {
                var m_itemField = typeof(SoL.Game.UI.Loot.LootRollItemUI).GetField("m_item", BindingFlags.NonPublic | BindingFlags.Instance);
                var lootRollItem = m_itemField.GetValue(slot) as SoL.Game.Loot.LootRollItem;
                if (slot.Occupied && lootRollItem != null && lootRollItem.Status == SoL.Game.Loot.LootRollStatus.Pending)
                {
                    if (lootRollItem.Instance != null && container.Instances.Contains(lootRollItem.Instance))
                        return true;
                }
            }

            return false;
        }

        private static bool HasLooted(ContainerInstance container) => _lootedContainers.TryGetValue(container, out _);
        private static void MarkAsLooted(ContainerInstance container) => _lootedContainers.GetValue(container, _ => new object());
    }
}