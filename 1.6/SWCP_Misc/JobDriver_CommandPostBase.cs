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
                    JumpToToil(reenterToil);
            };

            var loopBack = new Toil();
            loopBack.defaultCompleteMode = ToilCompleteMode.Instant;
            loopBack.initAction = () => JumpToToil(hold);

            yield return gotoToil;
            yield return hold;
            yield return reenterToil;
            yield return loopBack;
        }
    }
}
