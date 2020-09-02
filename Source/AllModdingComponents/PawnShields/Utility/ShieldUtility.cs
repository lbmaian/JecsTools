using Verse;

namespace PawnShields
{
    /// <summary>
    /// Utility functions and extensions for when dealing with shields.
    /// </summary>
    public static class ShieldUtility
    {
        public static CompShield GetCompShield(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                if (comps[i] is CompShield comp)
                    return comp;
            }
            return null;
        }

        /// <summary>
        /// Attempts to get the first shield from the pawn.
        /// </summary>
        /// <param name="pawn">Pawn to get shield from.</param>
        /// <returns>Shield if pawn has any or null if pawn can't have equipment or there is no shield.</returns>
        public static ThingWithComps GetShield(this Pawn pawn)
        {
            if (pawn.equipment == null)
                return null;
            return pawn.equipment.GetShield();
        }

        /// <summary>
        /// Attempts to get the first shield from the equipment tracker.
        /// </summary>
        /// <param name="eqTracker">Equipment tracker to get shield from.</param>
        /// <returns>Shield if tracker has any or null if there is no shield.</returns>
        public static ThingWithComps GetShield(this Pawn_EquipmentTracker eqTracker)
        {
            var allEquipment = eqTracker.AllEquipmentListForReading;
            for (int i = 0, count = allEquipment.Count; i < count; i++)
            {
                var equipment = allEquipment[i];
                if (equipment.GetCompShield() != null)
                    return equipment;
            }
            return null;
        }
    }
}
