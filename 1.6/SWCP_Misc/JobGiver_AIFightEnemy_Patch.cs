using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SWCP_Misc
{
    [HarmonyPatch(typeof(JobGiver_AIFightEnemy), "TryGiveJob")]
    public static class JobGiver_AIFightEnemy_Patch
    {

        public static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result != null) return;
            if (pawn?.Map == null || pawn.Faction == null) return;
            if (pawn.Downed || pawn.Dead || !pawn.RaceProps.Humanlike) return;

            var post = FindBestPostForPawn(pawn, out bool isDefending);
            if (post == null) return;

            IntVec3 standCell;
            if (!CellFinder.TryFindRandomCellNear(
                    post.parent.Position, pawn.Map,
                    Mathf.CeilToInt(post.Props.captureRadius),
                    c => c.Standable(pawn.Map)
                      && c.InHorDistOf(post.parent.Position, post.Props.captureRadius)
                      && pawn.CanReach(c, PathEndMode.OnCell, Danger.Some),
                    out standCell))
            {
                standCell = post.parent.Position;
            }

            __result = JobMaker.MakeJob(
                isDefending ? DefsOf.SWCP_DefendCommandPost : DefsOf.SWCP_CaptureCommandPost,
                post.parent, standCell);
        }

        private static List<ThingDef> cachedCommandPostDefs;
        public static List<ThingDef> CommandPostDefs => cachedCommandPostDefs ??= DefDatabase<ThingDef>.AllDefs.Where(d => d.comps != null && d.comps.Any(c => c is CompProperties_CommandPost)).ToList();

        private static CompCommandPost FindBestPostForPawn(Pawn pawn, out bool isDefending)
        {
            CompCommandPost bestDefend = null;
            CompCommandPost bestCapture = null;
            var bestDefendDist = float.MaxValue;
            var bestCaptureDist = float.MaxValue;

            foreach (var def in CommandPostDefs)
            {
                foreach (var thing in pawn.Map.listerThings.ThingsOfDef(def))
                {
                    var dist = pawn.Position.DistanceTo(thing.Position);
                    if (dist > 80f) continue;

                    if (!pawn.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some)) continue;

                    var post = thing.TryGetComp<CompCommandPost>();
                    if (post == null) continue;

                    if (thing.Faction == pawn.Faction)
                    {
                        if (post.CurrentState == CommandPostState.Reverting && dist < bestDefendDist)
                        {
                            bestDefendDist = dist;
                            bestDefend = post;
                        }
                    }
                    else if (thing.Faction == null || thing.Faction.HostileTo(pawn.Faction))
                    {
                        if (dist < bestCaptureDist)
                        {
                            bestCaptureDist = dist;
                            bestCapture = post;
                        }
                    }
                }
            }

            if (bestDefend != null) { isDefending = true; return bestDefend; }
            isDefending = false;
            return bestCapture;
        }
    }
}
