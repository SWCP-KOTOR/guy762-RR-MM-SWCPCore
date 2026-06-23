using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace SWCP_Misc
{
    public abstract class JobDriver_CommandPostBase : JobDriver
    {
        protected CompCommandPost cachedPostComp;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected abstract JobCondition? CheckEndCondition();

        public override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);

            var reenterToil = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);

            var gotoToil = Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell)
                .FailOn(() => !pawn.CanReach(job.targetB.Cell, PathEndMode.OnCell, Danger.Some));

            var hold = new Toil();
            hold.defaultCompleteMode = ToilCompleteMode.Never;
            hold.socialMode = RandomSocialMode.Off;
            hold.FailOnDespawnedOrNull(TargetIndex.A);
            hold.initAction = () => cachedPostComp = job.targetA.Thing?.TryGetComp<CompCommandPost>();
            hold.tickAction = () =>
            {
                var condition = CheckEndCondition();
                if (condition.HasValue)
                {
                    EndJobWith(condition.Value);
                    return;
                }

                if (!pawn.Position.InHorDistOf(job.targetA.Thing.Position, cachedPostComp.Props.captureRadius))
                {
                    JumpToToil(reenterToil);
                    return;
                }

                if (pawn.IsHashIntervalTick(4))
                {
                    CheckForAutoAttack();
                }
            };

            var loopBack = new Toil();
            loopBack.defaultCompleteMode = ToilCompleteMode.Instant;
            loopBack.initAction = () => JumpToToil(hold);

            yield return gotoToil;
            yield return hold;
            yield return reenterToil;
            yield return loopBack;
        }

       private void CheckForAutoAttack()
       {
           if (!pawn.kindDef.canMeleeAttack || pawn.Downed || pawn.stances.FullBodyBusy || pawn.IsCarryingPawn() || (!pawn.IsPlayerControlled && pawn.IsPsychologicallyInvisible()) || pawn.IsShambler)
           {
               return;
           }
           bool canMelee = !pawn.WorkTagIsDisabled(WorkTags.Violent);
           bool canFightFire = pawn.RaceProps.ToolUser && pawn.Faction == Faction.OfPlayer && !pawn.WorkTagIsDisabled(WorkTags.Firefighting);
           if (!(canMelee || canFightFire))
           {
               return;
           }
           Fire fire = null;
           for (int i = 0; i < 9; i++)
           {
               IntVec3 c = pawn.Position + GenAdj.AdjacentCellsAndInside[i];
               if (!c.InBounds(Map))
               {
                   continue;
               }
               List<Thing> thingList = c.GetThingList(Map);
               for (int j = 0; j < thingList.Count; j++)
               {
                   if (canMelee && pawn.kindDef.canMeleeAttack && thingList[j] is Pawn targetPawn && !targetPawn.ThreatDisabled(pawn) && pawn.HostileTo(targetPawn))
                   {
                       CompActivity comp = targetPawn.GetComp<CompActivity>();
                       if ((comp == null || comp.IsActive) && !pawn.ThreatDisabledBecauseNonAggressiveRoamer(targetPawn) && GenHostility.IsActiveThreatTo(targetPawn, pawn.Faction, ignoreHives: false))
                       {
                           pawn.meleeVerbs.TryMeleeAttack(targetPawn);
                           return;
                       }
                   }
                   if (canFightFire && thingList[j] is Fire fire2 && (fire == null || fire2.fireSize < fire.fireSize || i == 8) && (fire2.parent == null || fire2.parent != pawn))
                   {
                       fire = fire2;
                   }
               }
           }
           if (fire != null && (!pawn.InMentalState || pawn.MentalState.def.allowBeatfire))
           {
               pawn.natives.TryBeatFire(fire);
           }
           else
           {
               if (!canMelee || !job.canUseRangedWeapon || (pawn.drafter != null && !pawn.drafter.FireAtWill))
               {
                   return;
               }
               Verb currentEffectiveVerb = pawn.CurrentEffectiveVerb;
               if (currentEffectiveVerb != null && !currentEffectiveVerb.verbProps.IsMeleeAttack)
               {
                   TargetScanFlags targetScanFlags = TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable;
                   if (currentEffectiveVerb.IsIncendiary_Ranged())
                   {
                       targetScanFlags |= TargetScanFlags.NeedNonBurning;
                   }
                   Thing thing = (Thing)AttackTargetFinder.BestShootTargetFromCurrentPosition(pawn, targetScanFlags);
                   if (thing != null)
                   {
                       pawn.TryStartAttack(thing);
                   }
               }
           }
       }
   }
}
