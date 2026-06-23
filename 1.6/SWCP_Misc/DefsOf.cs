using RimWorld;
using Verse;

namespace SWCP_Misc
{
    [DefOf]
    public static class DefsOf
    {
        public static JobDef SWCP_CaptureCommandPost;
        public static JobDef SWCP_DefendCommandPost;

        static DefsOf() => DefOfHelper.EnsureInitializedInCtor(typeof(DefsOf));
    }
}
