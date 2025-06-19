using MelonLoader;
using HarmonyLib;
using System.Runtime.CompilerServices;
using SoL.Game.Objects.Containers;
using SoL.Game.Objects.Archetypes;
using SoL.Game.Transactions;
using SoL.Game;
using SoL;
using System.Collections;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;

namespace AutoLootMod
{
    public class AutoLootMod : MelonMod
    {
        ///Initialization///
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("[AutoLootMod] Initializing and patching ContainerInstance.Add");
            HarmonyInstance.PatchAll(typeof(AutoLootPatch_ContainerInstance));
        }
    }

    [HarmonyPatch(typeof(ContainerInstance), "Add")]
    public static class AutoLootPatch_ContainerInstance
    {
        ///Looted Containers Memory///
        private static ConditionalWeakTable<ContainerInstance, object> _lootedContainers = new ConditionalWeakTable<ContainerInstance, object>();

        ///Loot Container Postfix///
        static void Postfix(ContainerInstance __instance, ArchetypeInstance instance, bool update)
        {
            MelonLogger.Msg($"[AutoLootMod] Postfix triggered for ContainerInstance.Add | ContainerType: {__instance.ContainerType}, Instance: {instance}, Update: {update}");

            if (__instance.ContainerType != ContainerType.Loot)
            {
                MelonLogger.Msg("[AutoLootMod] Not a loot container, skipping.");
                return;
            }

            if (__instance.Count > 0 && !HasLooted(__instance))
            {
                MarkAsLooted(__instance);
                MelonLogger.Msg("[AutoLootMod] Marked container as looted.");
                MelonCoroutines.Start(AutoLootItemsCoroutine(__instance));
            }
        }

        ///Auto Loot Coroutine///
        private static IEnumerator AutoLootItemsCoroutine(ContainerInstance container)
        {
            var items = new List<ArchetypeInstance>(container.Instances);
            MelonLogger.Msg($"[AutoLootMod] Preparing to auto-loot {items.Count} items via TransferRequest/MergeRequest...");

            var collectionController = LocalPlayer.GameEntity?.CollectionController;
            if (collectionController == null)
            {
                MelonLogger.Error("[AutoLootMod] LocalPlayer.GameEntity.CollectionController is null!");
                yield break;
            }

            var controllerType = collectionController.GetType();

            ///Reflection Lookups///
            var processTransferRequest = controllerType.GetMethod("ProcessTransferRequest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (processTransferRequest == null)
            {
                MelonLogger.Error("[AutoLootMod] Could not find ProcessTransferRequest method via reflection!");
                yield break;
            }
            var processMergeRequest = controllerType.GetMethod("ProcessMergeRequest", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (processMergeRequest == null)
            {
                MelonLogger.Error("[AutoLootMod] Could not find ProcessMergeRequest method via reflection!");
                yield break;
            }
            var m_collectionsField = controllerType.GetField("m_collections", BindingFlags.Instance | BindingFlags.NonPublic);
            if (m_collectionsField == null)
            {
                MelonLogger.Error("[AutoLootMod] Could not find m_collections field via reflection!");
                yield break;
            }
            var m_collections = m_collectionsField.GetValue(collectionController) as IDictionary;
            if (m_collections == null)
            {
                MelonLogger.Error("[AutoLootMod] m_collections is null or not a dictionary!");
                yield break;
            }

            ///Inventory Discovery///
            ContainerInstance inventoryContainer = null;
            UniqueId mainBagId = UniqueId.Empty;
            foreach (DictionaryEntry entry in m_collections)
            {
                var key = entry.Key?.ToString();
                var value = entry.Value as ContainerInstance;
                MelonLogger.Msg($"[AutoLootMod] Container: Key={key}, Id={value?.Id}, Type={value?.ContainerType}");
                if (value != null && value.ContainerType == ContainerType.Inventory && mainBagId.IsEmpty)
                {
                    mainBagId = new UniqueId(value.Id);
                    inventoryContainer = value;
                }
            }
            if (mainBagId.IsEmpty || inventoryContainer == null)
            {
                MelonLogger.Error("[AutoLootMod] Could not find main bag/container ID or inventory container!");
                yield break;
            }
            UniqueId lootContainerId = new UniqueId(container.Id);

            var transferRequestType = typeof(TransferRequest);
            var mergeRequestType = typeof(MergeRequest);

            ///Main Item Loop///
            foreach (var item in items)
            {
                if (item == null)
                {
                    MelonLogger.Warning("[AutoLootMod] Null item, skipping.");
                    continue;
                }

                ///Stackable Detection///
                bool isStackable = item.Archetype is IStackable;
                ArchetypeInstance matchingStack = null;
                if (isStackable)
                {
                    foreach (var invItem in inventoryContainer.Instances)
                    {
                        if (invItem != null && invItem.ArchetypeId == item.ArchetypeId && invItem != item)
                        {
                            var maxStack = ((IStackable)invItem.Archetype).MaxStackCount;
                            int invCount = invItem.ItemData?.Count ?? 0;
                            int lootCount = item.ItemData?.Count ?? 0;
                            if (invCount < maxStack)
                            {
                                matchingStack = invItem;
                                MelonLogger.Msg($"[AutoLootMod] Found matching stack in inventory for {item.Archetype?.DisplayName}: InventoryCount={invCount}, LootCount={lootCount}, MaxStack={maxStack}");
                                break;
                            }
                        }
                    }
                }

                ///Merge///
                if (isStackable && matchingStack != null)
                {
                    MelonLogger.Msg($"[AutoLootMod] Sending MergeRequest for {item.Archetype?.DisplayName}: SourceContainer={container.Id}, SourceIndex={item.Index}, TargetContainer={inventoryContainer.Id}, TargetIndex={matchingStack.Index}");
                    var mergeRequest = System.Activator.CreateInstance(mergeRequestType);
                    mergeRequestType.GetField("SourceContainer")?.SetValue(mergeRequest, container.Id);
                    mergeRequestType.GetField("SourceIndex")?.SetValue(mergeRequest, item.Index);
                    mergeRequestType.GetField("TargetContainer")?.SetValue(mergeRequest, inventoryContainer.Id);
                    mergeRequestType.GetField("TargetIndex")?.SetValue(mergeRequest, matchingStack.Index);
                    mergeRequestType.GetField("TransactionId")?.SetValue(mergeRequest, UniqueId.GenerateFromGuid());
                    int countToMove = item.ItemData?.Count ?? 1;
                    mergeRequestType.GetField("Count")?.SetValue(mergeRequest, countToMove);
                    processMergeRequest.Invoke(collectionController, new object[] { mergeRequest });
                    MelonLogger.Msg("[AutoLootMod] MergeRequest sent.");
                }
                else
                {
                    ///Transfer///
                    MelonLogger.Msg($"[AutoLootMod] Sending TransferRequest for {item.Archetype?.DisplayName}: SourceContainer={container.Id}, TargetContainer={mainBagId.Value}, InstanceId={item.InstanceId} (Instance SET)");
                    var transferRequest = System.Activator.CreateInstance(transferRequestType);
                    transferRequestType.GetField("SourceContainer")?.SetValue(transferRequest, container.Id);
                    transferRequestType.GetField("TargetContainer")?.SetValue(transferRequest, mainBagId.Value);
                    transferRequestType.GetField("InstanceId")?.SetValue(transferRequest, item.InstanceId);
                    transferRequestType.GetField("TransactionId")?.SetValue(transferRequest, UniqueId.GenerateFromGuid());
                    transferRequestType.GetField("TargetIndex")?.SetValue(transferRequest, -1);
                    transferRequestType.GetField("Instance")?.SetValue(transferRequest, item);
                    processTransferRequest.Invoke(collectionController, new object[] { transferRequest });
                    MelonLogger.Msg("[AutoLootMod] TransferRequest sent (Instance SET).");

                    yield return new UnityEngine.WaitForSeconds(0.1f);

                    MelonLogger.Msg($"[AutoLootMod] Sending TransferRequest for {item.Archetype?.DisplayName}: SourceContainer={container.Id}, TargetContainer={mainBagId.Value}, InstanceId={item.InstanceId} (Instance NULL)");
                    var transferRequestNull = System.Activator.CreateInstance(transferRequestType);
                    transferRequestType.GetField("SourceContainer")?.SetValue(transferRequestNull, container.Id);
                    transferRequestType.GetField("TargetContainer")?.SetValue(transferRequestNull, mainBagId.Value);
                    transferRequestType.GetField("InstanceId")?.SetValue(transferRequestNull, item.InstanceId);
                    transferRequestType.GetField("TransactionId")?.SetValue(transferRequestNull, UniqueId.GenerateFromGuid());
                    transferRequestType.GetField("TargetIndex")?.SetValue(transferRequestNull, -1);
                    transferRequestType.GetField("Instance")?.SetValue(transferRequestNull, null);
                    processTransferRequest.Invoke(collectionController, new object[] { transferRequestNull });
                    MelonLogger.Msg("[AutoLootMod] TransferRequest sent (Instance NULL).");
                }

                ///Yield Delay///
                yield return new UnityEngine.WaitForSeconds(0.15f);
            }
        }

        ///Looted Helper///
        private static bool HasLooted(ContainerInstance container) => _lootedContainers.TryGetValue(container, out _);
        private static void MarkAsLooted(ContainerInstance container) => _lootedContainers.GetValue(container, _ => new object());
    }
}