using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace SWCP_Misc
{
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.IsDefeated))]
    public static class SettlementDefeatUtility_IsDefeated_Patch
    {
        public static void Postfix(Map map, Faction faction, ref bool __result)
        {
            if (!__result)
            {
                return;
            }
            var hasActiveCommandPosts = JobGiver_AIFightEnemy_Patch.CommandPostDefs.Any(def =>
                map.listerThings.ThingsOfDef(def).Any(t => t.Faction == faction));
            if (hasActiveCommandPosts)
            {
                __result = false;
            }
        }
    }
}
