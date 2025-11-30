using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shadowbus
{
    public class BattleManagerPatcher
    {
        [HarmonyPatch(typeof(BattleManagerBase), "Update")]
        [HarmonyPrefix]
        public static bool Update(BattleManagerBase __instance, ref float dt)
        {
            Plugin.Logger.LogMessage($"Update: {dt}");
            return true;
        }
    }
}
