using RimWorld;
using Verse.AI;

namespace SWCP_Misc
{
    public class JobDriver_CaptureCommandPost : JobDriver_CommandPostBase
    {
        protected override JobCondition? CheckEndCondition()
        {
            if (job.targetA.Thing.Faction == pawn.Faction)
                return JobCondition.Succeeded;

            if (job.targetA.Thing.Faction != null &&
                !job.targetA.Thing.Faction.HostileTo(pawn.Faction))
                return JobCondition.Incompletable;

            return null;
        }
    }
}
