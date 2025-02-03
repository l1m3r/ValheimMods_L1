using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;

namespace AutoFuel
{
    [BepInPlugin("aedenthorn.AutoFuel", "Auto Fuel", "1.2.1")]
    public class BepInExPlugin: BaseUnityPlugin
    {
        public static ConfigEntry<int> nexusID;
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;

        public static ConfigEntry<bool> distributedFueling;
        public static ConfigEntry<bool> leaveLastItem;
        public static ConfigEntry<int> limitFuelAdding;
        public static ConfigEntry<string> fuelDisallowTypes;
        public static ConfigEntry<bool> refuelStandingTorches;
        public static ConfigEntry<bool> refuelWallTorches;
        public static ConfigEntry<bool> refuelFirePits;

        public static ConfigEntry<float> dropedFuelRange;
        public static ConfigEntry<float> fireplaceRange;
        public static ConfigEntry<float> smelterOreRange;
        public static ConfigEntry<float> smelterFuelRange;

        public static ConfigEntry<string> oreDisallowTypes;
        public static ConfigEntry<bool> restrictKilnOutput;
        public static ConfigEntry<int> restrictKilnOutputAmount;

        public static ConfigEntry<string> toggleKey;
        public static ConfigEntry<string> toggleString;
        public static ConfigEntry<bool> enabledDynamicByHKey;


        private static BepInExPlugin context;

        private static float lastFuelTime;
        private static int fuelCount; // What is fuelCount and it's related magic number 33 (in relation to delay between fueling?!)

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(BepInExPlugin).Namespace + " " : "") + str);
        }
        private void Awake()
        {
            context = this;

            nexusID =       Config.Bind<int>("#0_General", "NexusID", 159, "Nexus mod ID for updates");
            modEnabled =    Config.Bind<bool>("#0_General", "ModEnabled", true, "Enable this mod");
            isDebug =       Config.Bind<bool>("#0_General", "IsDebug", false, "Debug mode");

            distributedFueling =    Config.Bind<bool>("#1_Fueling", "distributedFueling", false, "If true, refilling will occur one piece of fuel or ore at a time, making filling take longer but be better distributed between objects.");
            leaveLastItem =         Config.Bind<bool>("#1_Fueling", "LeaveLastItem", true, "Don't use last of item in chest");
            limitFuelAdding =       Config.Bind<int>("#1_Fueling", "limitFuelAdding", 2, "Maximum number of fuel items AutoFuel is allowed to add.");
            fuelDisallowTypes =     Config.Bind<string>("#1_Fueling", "FuelDisallowTypes", "RoundLog,FineWood", "Types of item to disallow as fuel (i.e. anything that is consumed), comma-separated.");
            refuelStandingTorches = Config.Bind<bool>("#1_Fueling", "RefuelStandingTorches", true, "Refuel standing torches");
            refuelWallTorches =     Config.Bind<bool>("#1_Fueling", "RefuelWallTorches", true, "Refuel wall torches");
            refuelFirePits =        Config.Bind<bool>("#1_Fueling", "RefuelFirePits", true, "Refuel fire pits");

            dropedFuelRange =   Config.Bind<float>("#2_Ranges", "DropedFuelRange", 5f, "The maximum range to pull dropped fuel");
            fireplaceRange =    Config.Bind<float>("#2_Ranges", "FireplaceRange", 5f, "The maximum range to pull fuel from containers for fireplaces");
            smelterOreRange =   Config.Bind<float>("#2_Ranges", "SmelterOreRange", 5f, "The maximum range to pull fuel from containers for smelters");
            smelterFuelRange =  Config.Bind<float>("#2_Ranges", "SmelterFuelRange", 5f, "The maximum range to pull ore from containers for smelters");

            oreDisallowTypes =      Config.Bind<string>("#3_Kiln specific", "OreDisallowTypes", "RoundLog,FineWood", "Types of item to disallow as ore (i.e. anything that is transformed), comma-separated).");
            restrictKilnOutput =    Config.Bind<bool>("#3_Kiln specific", "RestrictKilnOutput", false, "Restrict kiln output");
            restrictKilnOutputAmount = Config.Bind<int>("#3_Kiln specific", "RestrictKilnOutputAmount", 10, "Amount of coal to shut off kiln fueling");

            toggleKey =             Config.Bind<string>("#4_Hotkey", "ToggleKey", "", "Key to toggle behaviour. Leave blank to disable the toggle key. Use https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            toggleString =          Config.Bind<string>("#4_Hotkey", "ToggleString", "Auto Fuel: {0}", "Text to show on toggle. {0} is replaced with true/false");
            enabledDynamicByHKey =  Config.Bind<bool>("#4_Hotkey", "EnabledDynamicByHKey", true, "Behaviour is currently on or not");

            if (!modEnabled.Value)
                return;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private void Update()
        {
            if (AedenthornUtils.CheckKeyDown(toggleKey.Value) && !AedenthornUtils.IgnoreKeyPresses(true))
            {
                enabledDynamicByHKey.Value = !enabledDynamicByHKey.Value;
                Config.Save();
                Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    string.Format(toggleString.Value,enabledDynamicByHKey.Value),
                    0, null);
            }

        }
        private static string GetPrefabName(string name)
        {
            char[] anyOf = new char[]{'(',' '};
            int num = name.IndexOfAny(anyOf);
            string result;
            if (num >= 0)
                result = name.Substring(0, num);
            else
                result = name;
            return result;
        }

        public static List<Container> GetNearbyContainers(Vector3 center, float range)
        {
            try { 
                List<Container> containers = new List<Container>();

                foreach (Collider collider in Physics.OverlapSphere(center, Mathf.Max(range, 0), LayerMask.GetMask(new string[] { "piece" })))
                {
                    Container container = GetContainer(collider.transform);
                    if (container is null || container.GetComponent<ZNetView>()?.IsValid() != true)
                        continue;
                    if (container.GetInventory() != null)
                    {
                        containers.Add(container);
                    }
                }
                return containers;
            }
            catch
            {
                return new List<Container>();
            }
        }

        private static Container GetContainer(Transform transform)
        {
            while(transform != null)
            {
                Container c = transform.GetComponent<Container>();
                if (c != null)
                    return c;
                transform = transform.parent;
            }
            return null;
        }

        [HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
        static class Fireplace_UpdateFireplace_Patch
        {
            static void Postfix(Fireplace __instance, ZNetView ___m_nview)
            {
                if (!Player.m_localPlayer
                    || !enabledDynamicByHKey.Value
                    || !___m_nview.IsOwner()
                    || (__instance.name.Contains("groundtorch") && !refuelStandingTorches.Value)
                    || (__instance.name.Contains("walltorch") && !refuelWallTorches.Value)
                    || (__instance.name.Contains("fire_pit") && !refuelFirePits.Value))
                    return;

                // WhyTF is this triggering constantly?
                // Maybe trigger only on specific events - like a fireplace fuel reserves going down?
                // For empty fire places if something was dropped in the area (by the player) or a chest gets updated?
                Dbgl($"times: current{Time.time} - lastFT{lastFuelTime} = {Time.time - lastFuelTime}  |  fuelCount={fuelCount}");
                if (Time.time - lastFuelTime < 0.1) {
                    fuelCount++;
                    RefuelTorch(__instance, ___m_nview, fuelCount * 33);
                } else {
                    fuelCount = 0;
                    lastFuelTime = Time.time;
                    RefuelTorch(__instance, ___m_nview, fuelCount);
                }
            }
        }

        public static async void RefuelTorch(Fireplace fireplace, ZNetView znview, int delay)
        {
            try
            {
                await Task.Delay(delay);

                if (!fireplace || !znview || !znview.IsValid() || !modEnabled.Value)
                    return;

                // current amount of fuel in "fireplace".
                int maxFuelToAdd = (int)(Mathf.Ceil(znview.GetZDO().GetFloat("fuel", 0f)));
                Dbgl($"maxFuelToAdd: {maxFuelToAdd} - fuel reserves {fireplace.transform.position}");
                
                if (maxFuelToAdd > limitFuelAdding.Value)
                    return; // still enough fuel in "fireplace".

                // fuel-space left in "fireplace"..
                maxFuelToAdd = (int)(fireplace.m_maxFuel) - maxFuelToAdd;
                Dbgl($"maxFuelToAdd: {maxFuelToAdd} - space left");

                // allowed amount of fuel to be added by AutoFuel.
                if (maxFuelToAdd > limitFuelAdding.Value)
                    maxFuelToAdd = limitFuelAdding.Value;
                Dbgl($"maxFuelToAdd: {maxFuelToAdd} - allowed to add (limit={limitFuelAdding.Value})");

                List<Container> nearbyContainers = GetNearbyContainers(fireplace.transform.position, fireplaceRange.Value);

                Vector3 position = fireplace.transform.position + Vector3.up;
                foreach (Collider collider in Physics.OverlapSphere(position, dropedFuelRange.Value, LayerMask.GetMask(new string[] { "item" })))
                {
                    if (collider?.attachedRigidbody)
                    {
                        ItemDrop item = collider.attachedRigidbody.GetComponent<ItemDrop>();
                        //Dbgl($"nearby item name: {item.m_itemData.m_dropPrefab.name}");

                        if (item?.GetComponent<ZNetView>()?.IsValid() != true)
                            continue;

                        string name = GetPrefabName(item.gameObject.name);

                        if (item.m_itemData.m_shared.m_name == fireplace.m_fuelItem.m_itemData.m_shared.m_name && maxFuelToAdd > 0)
                        {

                            if (fuelDisallowTypes.Value.Split(',').Contains(name))
                            {
                                //Dbgl($"ground has {item.m_itemData.m_dropPrefab.name} but it's forbidden by config");
                                continue;
                            }

                            Dbgl($"auto adding fuel {name} from ground");

                            int amount = Mathf.Min(item.m_itemData.m_stack, maxFuelToAdd);
                            maxFuelToAdd -= amount;

                            for (int i = 0; i < amount; i++)
                            {
                                if (item.m_itemData.m_stack <= 1)
                                {
                                    if (znview.GetZDO() == null)
                                        Destroy(item.gameObject);
                                    else
                                        ZNetScene.instance.Destroy(item.gameObject);
                                    znview.InvokeRPC("RPC_AddFuel", new object[] { });
                                    if (distributedFueling.Value)
                                        return;
                                    break;

                                }

                                item.m_itemData.m_stack--;
                                znview.InvokeRPC("RPC_AddFuel", new object[] { });
                                Traverse.Create(item).Method("Save").GetValue();
                                if (distributedFueling.Value)
                                    return;
                            }
                        }
                    }
                }
                foreach (Container cntnr in nearbyContainers)
                {
                    if (fireplace.m_fuelItem && maxFuelToAdd > 0)
                    {
                        List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                        cntnr.GetInventory().GetAllItems(fireplace.m_fuelItem.m_itemData.m_shared.m_name, itemList);

                        foreach (var fuelItem in itemList)
                        {
                            if (fuelItem != null && (!leaveLastItem.Value || fuelItem.m_stack > 1))
                            {
                                if (fuelDisallowTypes.Value.Split(',').Contains(fuelItem.m_dropPrefab.name))
                                {
                                    Dbgl($"container at {cntnr.transform.position} has {fuelItem.m_stack} {fuelItem.m_dropPrefab.name} but it's forbidden by config");
                                    continue;
                                }
                                maxFuelToAdd--;

                                Dbgl($"container at {cntnr.transform.position} has {fuelItem.m_stack} {fuelItem.m_dropPrefab.name}, taking one");

                                znview.InvokeRPC("RPC_AddFuel", new object[] { });

                                cntnr.GetInventory().RemoveItem(fireplace.m_fuelItem.m_itemData.m_shared.m_name, 1);
                                typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr, new object[] { });
                                typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr.GetInventory(), new object[] { });
                                if (distributedFueling.Value)
                                    return;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(Smelter), "UpdateSmelter")]
        static class Smelter_FixedUpdate_Patch
        {
            static void Postfix(Smelter __instance, ZNetView ___m_nview)
            {
                if (!Player.m_localPlayer || !enabledDynamicByHKey.Value || ___m_nview == null || !___m_nview.IsOwner())
                    return;

                if (Time.time - lastFuelTime < 0.1)
                {
                    fuelCount++;
                    RefuelSmelter(__instance, ___m_nview, fuelCount * 33);
                }
                else
                {
                    fuelCount = 0;
                    lastFuelTime = Time.time;
                    RefuelSmelter(__instance, ___m_nview, fuelCount);
                }

            }
        }

        public static async void RefuelSmelter(Smelter __instance, ZNetView ___m_nview, int delay)
        {

            await Task.Delay(delay);

            if (!__instance || !___m_nview || !___m_nview.IsValid() || !modEnabled.Value)
                return;

            int maxOre = __instance.m_maxOre - Traverse.Create(__instance).Method("GetQueueSize").GetValue<int>();
            int maxFuel = __instance.m_maxFuel - Mathf.CeilToInt(___m_nview.GetZDO().GetFloat("fuel", 0f));


            List<Container> nearbyOreContainers = GetNearbyContainers(__instance.transform.position, smelterOreRange.Value);
            List<Container> nearbyFuelContainers = GetNearbyContainers(__instance.transform.position, smelterFuelRange.Value);

            if (__instance.name.Contains("charcoal_kiln") && restrictKilnOutput.Value) {
                string outputName = __instance.m_conversion[0].m_to.m_itemData.m_shared.m_name;
                int maxOutput = restrictKilnOutputAmount.Value - Traverse.Create(__instance).Method("GetQueueSize").GetValue<int>();
                foreach (Container c in nearbyOreContainers)
                {
                    List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                    c.GetInventory().GetAllItems(outputName, itemList);

                    foreach (var outputItem in itemList)
                    {
                        if (outputItem != null)
                            maxOutput -= outputItem.m_stack;
                    }
                }
                if (maxOutput < 0)
                    maxOutput = 0;
                if (maxOre > maxOutput)
                    maxOre = maxOutput;
            }

            bool fueled = false;
            bool ored = false;

            Vector3 position = __instance.transform.position + Vector3.up;
            foreach (Collider collider in Physics.OverlapSphere(position, dropedFuelRange.Value, LayerMask.GetMask(new string[] { "item" })))
            {
                if (collider?.attachedRigidbody)
                {
                    ItemDrop item = collider.attachedRigidbody.GetComponent<ItemDrop>();
                    //Dbgl($"nearby item name: {item.m_itemData.m_dropPrefab.name}");

                    if (item?.GetComponent<ZNetView>()?.IsValid() != true)
                        continue;

                    string name = GetPrefabName(item.gameObject.name);

                    foreach (Smelter.ItemConversion itemConversion in __instance.m_conversion)
                    {
                        if (ored)
                            break;
                        if (item.m_itemData.m_shared.m_name == itemConversion.m_from.m_itemData.m_shared.m_name && maxOre > 0)
                        {

                            if (oreDisallowTypes.Value.Split(',').Contains(name))
                            {
                                //Dbgl($"container at {c.transform.position} has {item.m_itemData.m_stack} {item.m_dropPrefab.name} but it's forbidden by config");
                                continue;
                            }

                            Dbgl($"auto adding ore {name} from ground");

                            int amount = Mathf.Min(item.m_itemData.m_stack, maxOre);
                            maxOre -= amount;

                            for (int i = 0; i < amount; i++)
                            {
                                if (item.m_itemData.m_stack <= 1)
                                {
                                    if (___m_nview.GetZDO() == null)
                                        Destroy(item.gameObject);
                                    else
                                        ZNetScene.instance.Destroy(item.gameObject);
                                    ___m_nview.InvokeRPC("RPC_AddOre", new object[] { name });
                                    if (distributedFueling.Value)
                                        ored = true;
                                    break;
                                }

                                item.m_itemData.m_stack--;
                                ___m_nview.InvokeRPC("RPC_AddOre", new object[] { name });
                                Traverse.Create(item).Method("Save").GetValue();
                                if (distributedFueling.Value)
                                    ored = true;
                            }
                        }
                    }

                    if (__instance.m_fuelItem && item.m_itemData.m_shared.m_name == __instance.m_fuelItem.m_itemData.m_shared.m_name && maxFuel > 0 && !fueled)
                    {

                        if (fuelDisallowTypes.Value.Split(',').Contains(name))
                        {
                            //Dbgl($"ground has {item.m_itemData.m_dropPrefab.name} but it's forbidden by config");
                            continue;
                        }

                        Dbgl($"auto adding fuel {name} from ground");

                        int amount = Mathf.Min(item.m_itemData.m_stack, maxFuel);
                        maxFuel -= amount;

                        for (int i = 0; i < amount; i++)
                        {
                            if (item.m_itemData.m_stack <= 1)
                            {
                                if (___m_nview.GetZDO() == null)
                                    Destroy(item.gameObject);
                                else
                                    ZNetScene.instance.Destroy(item.gameObject);
                                ___m_nview.InvokeRPC("RPC_AddFuel", new object[] { });
                                if (distributedFueling.Value)
                                    fueled = true;
                                break;

                            }

                            item.m_itemData.m_stack--;
                            ___m_nview.InvokeRPC("RPC_AddFuel", new object[] { });
                            Traverse.Create(item).Method("Save").GetValue();
                            if (distributedFueling.Value)
                            {
                                fueled = true;
                                break;
                            }
                        }
                    }
                }
            }

            foreach (Container c in nearbyOreContainers)
            {
                foreach (Smelter.ItemConversion itemConversion in __instance.m_conversion)
                {
                    if (ored)
                        break;
                    List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                    c.GetInventory().GetAllItems(itemConversion.m_from.m_itemData.m_shared.m_name, itemList);

                    foreach (var oreItem in itemList)
                    {
                        if (oreItem != null && maxOre > 0 && (!leaveLastItem.Value || oreItem.m_stack > 1))
                        {
                            if (oreDisallowTypes.Value.Split(',').Contains(oreItem.m_dropPrefab.name))
                                continue;
                            maxOre--;

                            Dbgl($"container at {c.transform.position} has {oreItem.m_stack} {oreItem.m_dropPrefab.name}, taking one");

                            ___m_nview.InvokeRPC("RPC_AddOre", new object[] { oreItem.m_dropPrefab?.name });
                            c.GetInventory().RemoveItem(itemConversion.m_from.m_itemData.m_shared.m_name, 1);
                            typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { });
                            typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c.GetInventory(), new object[] { });
                            if (distributedFueling.Value)
                            {
                                ored = true;
                                break;
                            }
                        }
                    }
                }
            }
            foreach (Container c in nearbyFuelContainers)
            {
                if (!__instance.m_fuelItem || maxFuel <= 0 || fueled)
                    break;

                List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                c.GetInventory().GetAllItems(__instance.m_fuelItem.m_itemData.m_shared.m_name, itemList);

                foreach (var fuelItem in itemList)
                {
                    if (fuelItem != null && (!leaveLastItem.Value || fuelItem.m_stack > 1))
                    {
                        maxFuel--;
                        if (fuelDisallowTypes.Value.Split(',').Contains(fuelItem.m_dropPrefab.name))
                        {
                            //Dbgl($"container at {c.transform.position} has {item.m_stack} {item.m_dropPrefab.name} but it's forbidden by config");
                            continue;
                        }

                        Dbgl($"container at {c.transform.position} has {fuelItem.m_stack} {fuelItem.m_dropPrefab.name}, taking one");

                        ___m_nview.InvokeRPC("RPC_AddFuel", new object[] { });

                        c.GetInventory().RemoveItem(__instance.m_fuelItem.m_itemData.m_shared.m_name, 1);
                        typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { });
                        typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c.GetInventory(), new object[] { });
                        if (distributedFueling.Value)
                        {
                            fueled = true;
                            break;
                        }
                    }
                }
            }
        }
        [HarmonyPatch(typeof(Terminal), "InputText")]
        static class InputText_Patch
        {
            static bool Prefix(Terminal __instance)
            {
                if (!modEnabled.Value)
                    return true;
                string text = __instance.m_input.text;
                if (text.ToLower().Equals($"{typeof(BepInExPlugin).Namespace.ToLower()} reset"))
                {
                    context.Config.Reload();
                    context.Config.Save();

                    __instance.AddString(text);
                    __instance.AddString($"{context.Info.Metadata.Name} config reloaded");
                    return false;
                }
                return true;
            }
        }
    }
}
