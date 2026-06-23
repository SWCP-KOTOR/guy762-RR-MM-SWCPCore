using Verse.AI;

namespace SWCP_Misc
{
    public class JobDriver_DefendCommandPost : JobDriver_CommandPostBase
    {
        protected override JobCondition? CheckEndCondition()
        {
            if (cachedPostComp.CurrentState == CommandPostState.Held)
                return JobCondition.Succeeded;

            if (job.targetA.Thing.Faction != pawn.Faction)
                return JobCondition.Incompletable;

            return null;
        }
    }
}
