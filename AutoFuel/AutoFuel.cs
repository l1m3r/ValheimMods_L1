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
    [BepInPlugin("aedenthorn.AutoFuel", "Auto Fuel", "1.2.2")]
    public class BepInExPlugin: BaseUnityPlugin
    {
        public static ConfigEntry<int> nexusID;
        public static ConfigEntry<bool> modEnabled; // in combination with toggleState this confuses me (what is checked where and why).
        public static ConfigEntry<bool> isDebug;

        public static ConfigEntry<bool> distributedFilling;
        public static ConfigEntry<bool> leaveLastItem;
        public static ConfigEntry<int> limitFuelAdding;
        public static ConfigEntry<string> fuelDisallowTypes;
        public static ConfigEntry<bool> refuelStandingTorches;
        public static ConfigEntry<bool> refuelWallTorches;
        public static ConfigEntry<bool> refuelFirePits;

        public static ConfigEntry<float> droppedFuelRange;
        public static ConfigEntry<float> cntnrRangeFires;
        public static ConfigEntry<float> cntnrRangeSmltrOre;
        public static ConfigEntry<float> cntnrRangeSmltrFuel;

        public static ConfigEntry<string> oreDisallowTypes;
        public static ConfigEntry<bool> restrictKilnFill;
        public static ConfigEntry<int> restrictKilnFillAmount;

        public static ConfigEntry<string> toggleKey;
        public static ConfigEntry<string> toggleString;
        public static ConfigEntry<bool> toggleState;

        private static BepInExPlugin context;

        private static float lastFuelTime;
        private static int fuelCount; // What is fuelCount and it's related magic number 33 (in relation to delay between fueling?!)

        private static readonly string strNL = Environment.NewLine;
        private static readonly int rangeGlobMin = 0;
        private static readonly int rangeGlobMax = 20;
        private static readonly string limitMinMax = $"{strNL}Accepted range: {rangeGlobMin}<X<={rangeGlobMax}. Feature disabled when outside that range.";

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(BepInExPlugin).Namespace + ": " : "__: ") + str);
        }
        private void Awake()
        {
            context = this;

            nexusID =    Config.Bind<int>("#0_General", "NexusID", 159,
                "Nexus mod ID for updates.");
            modEnabled = Config.Bind<bool>("#0_General", "ModEnabled", true,
                "Enable this mod.");
            isDebug =    Config.Bind<bool>("#0_General", "IsDebug", false,
                "Toggle debug mode.");

            distributedFilling =    Config.Bind<bool>("#1_Fueling", "DistributedFilling", false,
                $"If enabled, refilling will occur one piece of fuel/ore at a time,{strNL}making filling take longer but be better distributed between objects.");
            leaveLastItem =         Config.Bind<bool>("#1_Fueling", "LeaveLastItem", true,
                "Don't use last of item in chests/containers.");
            limitFuelAdding =       Config.Bind<int>("#1_Fueling", "LimitFuelAdding", 1,
                $"Torches/fireplaces get filled up to this amount +1.{strNL}(smelters, kilns & refineries are exempt).");
            fuelDisallowTypes =     Config.Bind<string>("#1_Fueling", "FuelDisallowTypes", "RoundLog,FineWood,GreydwarfEye,Guck",
                $"Types of item to disallow as fuel/ore (i.e. anything that is consumed).{strNL}Comma-separated list (Wood,Resin,Sap,...).");
            refuelStandingTorches = Config.Bind<bool>("#1_Fueling", "RefuelStandingTorches", true,
                "Refuel standing torches.");
            refuelWallTorches =     Config.Bind<bool>("#1_Fueling", "RefuelWallTorches", true,
                "Refuel wall torches.");
            refuelFirePits =        Config.Bind<bool>("#1_Fueling", "RefuelFirePits", true,
                "Refuel fire pits.");

            cntnrRangeFires =     Config.Bind<float>("#2_Ranges", "CntnrRangeFuel4fires", 2f,
                "Max distance to pull fuel from container for fireplaces & torches." + limitMinMax);
            cntnrRangeSmltrFuel = Config.Bind<float>("#2_Ranges", "CntnrRangeFuel4smelter_refinery", 2.5f,
                "Max distance to pull fuel from container for smelters & refineries." + limitMinMax);
            cntnrRangeSmltrOre =  Config.Bind<float>("#2_Ranges", "CntnrRangeOre4smelter_refinery_kiln", 2.5f,
                "Max distance to pull ore from container for smelters, kilns & refineries." + limitMinMax);
            droppedFuelRange =    Config.Bind<float>("#2_Ranges", "DropedFuOrelRange", 3f,
                "Max distance to take dropped fuel/ore." + limitMinMax);

            oreDisallowTypes =       Config.Bind<string>("#3_specific_to_KilnSmelterRefinery", "OreDisallowTypes", "RoundLog,FineWood",
                $"Types of item to disallow as ore (i.e. anything that is transformed).{strNL}Comma-separated list with:{strNL}" +
                $"Softtissue, TinOre, CopperOre, CopperScrap, BronzeScrap, IronOre, IronScrap, SilverOre, Pickable_BogIronOre, BlackMetalScrap, FlametalOre, FlametalOreNew");
            restrictKilnFill =       Config.Bind<bool>("#3_specific_to_KilnSmelterRefinery", "RestrictKilnFill", false,
                "Enable to limit how much wood kilns can process when no player is around.");
            restrictKilnFillAmount = Config.Bind<int>("#3_specific_to_KilnSmelterRefinery", "RestrictKilnFillAmount", 10,
                $"Max amount of wood to be put in kilns.{strNL}This limits what kilns can process in absence of any players.");

            toggleKey =    Config.Bind<string>("#4_Hotkey", "ToggleKey", "",
                $"Key to toggle behavior. Leave blank to disable the toggle key.{strNL}" +
                $"Use https://docs.unity3d.com/Manual/ConventionalGameInput.html");
            toggleState =  Config.Bind<bool>("#4_Hotkey", "ToggleState", true,
                "Current 'mod-enabled' state - toggled by hotkey.");
            toggleString = Config.Bind<string>("#4_Hotkey", "ToggleString", "Auto Fuel: {0}",
                "Text to show on toggle. {0} is replaced with true/false.");

            if (!modEnabled.Value)
                return;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }

        private void Update()
        {
            if (AedenthornUtils.CheckKeyDown(toggleKey.Value) && !AedenthornUtils.IgnoreKeyPresses(true))
            {
                toggleState.Value = !toggleState.Value;
                Config.Save();
                Player.m_localPlayer.Message(MessageHud.MessageType.Center,
                    string.Format(toggleString.Value,toggleState.Value),
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
            static void Postfix(Fireplace __instanceFireplace, ZNetView ___m_nview)
            {
                if (!Player.m_localPlayer
                    || !toggleState.Value
                    || !___m_nview.IsOwner()
                    || (__instanceFireplace.name.Contains("groundtorch") && !refuelStandingTorches.Value)
                    || (__instanceFireplace.name.Contains("walltorch") && !refuelWallTorches.Value)
                    || (__instanceFireplace.name.Contains("fire_pit") && !refuelFirePits.Value)
                    )
                    return;

                // WhyTF is this triggering constantly?
                // Maybe trigger only on specific events - like a __instanceFireplace fuel reserves going down?
                // For empty fire places if something was dropped in the area (by the player) or a chest gets updated?
                Dbgl($"FP-times: current{Time.time} - lastFT{lastFuelTime} = {Time.time - lastFuelTime}  |  fuelCount?={fuelCount}");
                if (Time.time - lastFuelTime < 0.1) {
                    fuelCount++;
                    RefuelTorch(__instanceFireplace, ___m_nview, fuelCount * 33);
                } else {
                    fuelCount = 0;
                    lastFuelTime = Time.time;
                    RefuelTorch(__instanceFireplace, ___m_nview, fuelCount);
                }
            }
        }

        public static async void RefuelTorch(Fireplace __instanceFireplace, ZNetView znview, int delay)
        {
            try
            {
                await Task.Delay(delay);

                if (!__instanceFireplace || !znview || !znview.IsValid() || !modEnabled.Value)
                    return;

                // maxFuelToAdd = current -> space left -> maxToAdd
                // current amount of fuel in "__instanceFireplace".
                int maxFuelToAdd = (int)(Mathf.Ceil(znview.GetZDO().GetFloat("fuel", 0f)));
                Dbgl($"maxFuelToAdd: {maxFuelToAdd} = fuel reserves in {__instanceFireplace.name}@{__instanceFireplace.transform.position}.");
                
                if (maxFuelToAdd > limitFuelAdding.Value) return; // still enough fuel in "__instanceFireplace".

                // fuel-space left in "__instanceFireplace"..
                maxFuelToAdd = (int)(__instanceFireplace.m_maxFuel) - maxFuelToAdd;
                Dbgl($"maxFuelToAdd: {maxFuelToAdd} = space left.");

                // allowed amount of fuel to be added by AutoFuel.
                if (maxFuelToAdd > limitFuelAdding.Value) maxFuelToAdd = limitFuelAdding.Value;
                Dbgl($"maxFuelToAdd: {maxFuelToAdd} = allowed to add (limit={limitFuelAdding.Value}).");

                // Refill Fireplaces/Torches with dropped fuel.
                if (rangeGlobMin < droppedFuelRange.Value & droppedFuelRange.Value <= rangeGlobMax)
                {
                    foreach (Collider clldr in Physics.OverlapSphere(
                        __instanceFireplace.transform.position + Vector3.up,
                        droppedFuelRange.Value,
                        LayerMask.GetMask(new string[] { "item" })
                        ))
                    {
                        if (clldr?.attachedRigidbody)
                        {
                            ItemDrop item = clldr.attachedRigidbody.GetComponent<ItemDrop>();
                            //Dbgl($"nearby item name: {item.m_itemData.m_dropPrefab.name}");

                            if (item?.GetComponent<ZNetView>()?.IsValid() != true)
                                continue;

                            string name = GetPrefabName(item.gameObject.name);

                            if (item.m_itemData.m_shared.m_name == __instanceFireplace.m_fuelItem.m_itemData.m_shared.m_name && maxFuelToAdd > 0)
                            {

                                if (fuelDisallowTypes.Value.Split(',').Contains(name))
                                {
                                    Dbgl($"ground has {item.m_itemData.m_dropPrefab.name} but it's forbidden by config.");
                                    continue;
                                }

                                Dbgl($"auto moving fuel {name} from ground into {__instanceFireplace.name}@{__instanceFireplace.transform.position}.");

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
                                        if (distributedFilling.Value)
                                            return;
                                        break;
                                    }

                                    item.m_itemData.m_stack--;
                                    znview.InvokeRPC("RPC_AddFuel", new object[] { });
                                    Traverse.Create(item).Method("Save").GetValue();
                                    if (distributedFilling.Value)
                                        return;
                                }
                            }
                        }
                    }
                }

                // Refill Fireplaces/Torches from chests.
                if (cntnrRangeFires.Value > rangeGlobMax) cntnrRangeFires.Value = -1;
                foreach (Container cntnr in GetNearbyContainers(__instanceFireplace.transform.position, cntnrRangeFires.Value))
                {
                    if (__instanceFireplace.m_fuelItem && maxFuelToAdd > 0)
                    {
                        List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                        cntnr.GetInventory().GetAllItems(__instanceFireplace.m_fuelItem.m_itemData.m_shared.m_name, itemList);

                        foreach (var fuelItem in itemList)
                        {
                            if (fuelItem != null && (!leaveLastItem.Value || fuelItem.m_stack > 1))
                            {
                                if (fuelDisallowTypes.Value.Split(',').Contains(fuelItem.m_dropPrefab.name))
                                {
                                    Dbgl($"cntnr@{cntnr.transform.position} has {fuelItem.m_stack} {fuelItem.m_dropPrefab.name} but it's forbidden by config");
                                    continue;
                                }
                                maxFuelToAdd--;

                                Dbgl($"cntnr@{cntnr.transform.position} has {fuelItem.m_stack} {fuelItem.m_dropPrefab.name}, moving one into {__instanceFireplace.name}@{__instanceFireplace.transform.position}.");

                                znview.InvokeRPC("RPC_AddFuel", new object[] { });

                                cntnr.GetInventory().RemoveItem(__instanceFireplace.m_fuelItem.m_itemData.m_shared.m_name, 1);
                                typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr, new object[] { });
                                typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr.GetInventory(), new object[] { });
                                if (distributedFilling.Value)
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
                if (!Player.m_localPlayer
                    || !toggleState.Value
                    || ___m_nview == null
                    || !___m_nview.IsOwner()
                    )
                    return;

                Dbgl($"SM-times: current{Time.time} - lastFT{lastFuelTime} = {Time.time - lastFuelTime}  |  fuelCount?={fuelCount}");
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

        public static async void RefuelSmelter(Smelter __instanceSmltr, ZNetView ___m_nview, int delay)
        {

            await Task.Delay(delay);

            if (!__instanceSmltr || !___m_nview || !___m_nview.IsValid() || !modEnabled.Value)
                return;

            int maxOre = __instanceSmltr.m_maxOre - Traverse.Create(__instanceSmltr).Method("GetQueueSize").GetValue<int>();
            int maxFuel = __instanceSmltr.m_maxFuel - Mathf.CeilToInt(___m_nview.GetZDO().GetFloat("fuel", 0f));

            if (cntnrRangeSmltrOre.Value > rangeGlobMax) cntnrRangeSmltrOre.Value = -1;
            if (cntnrRangeSmltrFuel.Value > rangeGlobMax) cntnrRangeSmltrFuel.Value = -1;
            List<Container> nearbyOreContainers = GetNearbyContainers(__instanceSmltr.transform.position, cntnrRangeSmltrOre.Value);
            List<Container> nearbyFuelContainers = GetNearbyContainers(__instanceSmltr.transform.position, cntnrRangeSmltrFuel.Value);

            // extra check for Kiln fill limit (sets maxOre).
            if (__instanceSmltr.name.Contains("charcoal_kiln") && restrictKilnFill.Value && restrictKilnFillAmount.Value >= 0) {
                string outputName = __instanceSmltr.m_conversion[0].m_to.m_itemData.m_shared.m_name;
                int maxKilnFill = restrictKilnFillAmount.Value - Traverse.Create(__instanceSmltr).Method("GetQueueSize").GetValue<int>();
                foreach (Container cntnr in nearbyOreContainers)
                {
                    List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                    cntnr.GetInventory().GetAllItems(outputName, itemList);

                    foreach (var outputItem in itemList)
                    {
                        if (outputItem != null)
                            maxKilnFill -= outputItem.m_stack;
                    }
                }
                if (maxKilnFill < 0) maxKilnFill = 0;
                if (maxOre > maxKilnFill) maxOre = maxKilnFill;
            }

            bool fueled = false;
            bool ored = false;

            // RefuelSmelters with dropped fuel/ore.
            if (rangeGlobMin < droppedFuelRange.Value && droppedFuelRange.Value <= rangeGlobMax)
            {
                Vector3 position = __instanceSmltr.transform.position + Vector3.up;
                foreach (Collider clldr in Physics.OverlapSphere(position, droppedFuelRange.Value, LayerMask.GetMask(new string[] { "item" })))
                {
                    if (clldr?.attachedRigidbody)
                    {
                        ItemDrop item = clldr.attachedRigidbody.GetComponent<ItemDrop>();
                        //Dbgl($"nearby item name: {item.m_itemData.m_dropPrefab.name}");

                        if (item?.GetComponent<ZNetView>()?.IsValid() != true)
                            continue;

                        string name = GetPrefabName(item.gameObject.name);

                        foreach (Smelter.ItemConversion itemConversion in __instanceSmltr.m_conversion)
                        {
                            if (ored)
                                break;
                            if (item.m_itemData.m_shared.m_name == itemConversion.m_from.m_itemData.m_shared.m_name && maxOre > 0)
                            {
                                if (oreDisallowTypes.Value.Split(',').Contains(name))
                                {
                                    Dbgl($"Ground has {item.m_itemData.m_stack} {name} but it's forbidden by config.");
                                    continue;
                                }

                                Dbgl($"auto moving ore {name} from ground into {__instanceSmltr.name}@{__instanceSmltr.transform.position}.");

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
                                        if (distributedFilling.Value)
                                            ored = true;
                                        break;
                                    }

                                    item.m_itemData.m_stack--;
                                    ___m_nview.InvokeRPC("RPC_AddOre", new object[] { name });
                                    Traverse.Create(item).Method("Save").GetValue();
                                    if (distributedFilling.Value)
                                        ored = true;
                                }
                            }
                        }

                        if (__instanceSmltr.m_fuelItem && item.m_itemData.m_shared.m_name == __instanceSmltr.m_fuelItem.m_itemData.m_shared.m_name && maxFuel > 0 && !fueled)
                        {
                            if (fuelDisallowTypes.Value.Split(',').Contains(name))
                            {
                                Dbgl($"ground has {item.m_itemData.m_dropPrefab.name} but it's forbidden by config.");
                                continue;
                            }

                            Dbgl($"auto moving fuel {name} from ground into {__instanceSmltr.name}@{__instanceSmltr.transform.position}.");

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
                                    if (distributedFilling.Value)
                                        fueled = true;
                                    break;
                                }

                                item.m_itemData.m_stack--;
                                ___m_nview.InvokeRPC("RPC_AddFuel", new object[] { });
                                Traverse.Create(item).Method("Save").GetValue();
                                if (distributedFilling.Value)
                                {
                                    fueled = true;
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            foreach (Container cntnr in nearbyOreContainers)
            {
                foreach (Smelter.ItemConversion itemConversion in __instanceSmltr.m_conversion)
                {
                    if (ored)
                        break;
                    List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                    cntnr.GetInventory().GetAllItems(itemConversion.m_from.m_itemData.m_shared.m_name, itemList);

                    foreach (var oreItem in itemList)
                    {
                        if (oreItem != null && maxOre > 0 && (!leaveLastItem.Value || oreItem.m_stack > 1))
                        {
                            if (oreDisallowTypes.Value.Split(',').Contains(oreItem.m_dropPrefab.name))
                                continue;
                            maxOre--;

                            Dbgl($"cntnr@{cntnr.transform.position} has {oreItem.m_stack} {oreItem.m_dropPrefab.name}, moving one into {__instanceSmltr.name}@{__instanceSmltr.transform.position}.");

                            ___m_nview.InvokeRPC("RPC_AddOre", new object[] { oreItem.m_dropPrefab?.name });
                            cntnr.GetInventory().RemoveItem(itemConversion.m_from.m_itemData.m_shared.m_name, 1);
                            typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr, new object[] { });
                            typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr.GetInventory(), new object[] { });
                            if (distributedFilling.Value)
                            {
                                ored = true;
                                break;
                            }
                        }
                    }
                }
            }
            foreach (Container cntnr in nearbyFuelContainers)
            {
                if (!__instanceSmltr.m_fuelItem || maxFuel <= 0 || fueled)
                    break;

                List<ItemDrop.ItemData> itemList = new List<ItemDrop.ItemData>();
                cntnr.GetInventory().GetAllItems(__instanceSmltr.m_fuelItem.m_itemData.m_shared.m_name, itemList);

                foreach (var fuelItem in itemList)
                {
                    if (fuelItem != null && (!leaveLastItem.Value || fuelItem.m_stack > 1))
                    {
                        maxFuel--;
                        if (fuelDisallowTypes.Value.Split(',').Contains(fuelItem.m_dropPrefab.name))
                        {
                            Dbgl($"cntnr@{cntnr.transform.position} has {fuelItem.m_stack} {fuelItem.m_dropPrefab.name} but it's forbidden by config");
                            continue;
                        }

                        Dbgl($"cntnr@{cntnr.transform.position} has {fuelItem.m_stack} {fuelItem.m_dropPrefab.name}, moving one into {__instanceSmltr.name}@{__instanceSmltr.transform.position}.");

                        ___m_nview.InvokeRPC("RPC_AddFuel", new object[] { });

                        cntnr.GetInventory().RemoveItem(__instanceSmltr.m_fuelItem.m_itemData.m_shared.m_name, 1);
                        typeof(Container).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr, new object[] { });
                        typeof(Inventory).GetMethod("Changed", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cntnr.GetInventory(), new object[] { });
                        if (distributedFilling.Value)
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
