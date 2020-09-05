#define COMPS_FIELD_PUBLICIZED

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Verse;

namespace PawnShields
{
    /// <summary>
    /// Utility functions and extensions for when dealing with shields.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class ShieldUtility
    {
        private static readonly List<Func<Pawn_EquipmentTracker, ThingWithComps>> getShieldMethods =
            AccessTools.GetDeclaredMethods(typeof(ShieldUtility))
            .Where(method => method.IsPublic && method.Name.StartsWith("GetShield_"))
            // Add more filters here as wanted
            .Where(method => method.Name.Contains("_GetComps"))
            .Select(method => AccessTools.MethodDelegate<Func<Pawn_EquipmentTracker, ThingWithComps>>(method))
            .ToList();

        static ShieldUtility()
        {
            var harmony = new Harmony("shieldutilitytest");
            foreach (var func in getShieldMethods)
            {
                harmony.Patch(AccessTools.Method(typeof(ShieldUtility), func.Method.Name),
                    transpiler: new HarmonyMethod(typeof(ShieldUtility), nameof(PassthroughTranspiler)));
            }
            static bool IsNullaryGenericMethod(MethodBase method) => method.GetParameters().Length == 0 && method.GetGenericArguments().Length == 1;
            var methodsToInline = new[]
            {
                AccessTools.FirstMethod(typeof(ThingWithComps), method => method.Name == nameof(ThingWithComps.GetComp) && IsNullaryGenericMethod(method)),
                AccessTools.FirstMethod(typeof(ThingWithComps), method => method.Name == nameof(ThingWithComps.GetComps) && IsNullaryGenericMethod(method)),
                AccessTools.DeclaredMethod(typeof(ThingCompUtility), nameof(ThingCompUtility.TryGetComp), new[] { typeof(Thing) }),
                // Attempt to inline iterator internal methods.
                // XXX: This probably doesn't work since such internal methods implement an interface and are thus virtual.
                typeof(ThingWithComps).FindIteratorMethod(nameof(ThingWithComps.GetComps)),
                AccessTools.FirstProperty(typeof(ThingWithComps).FindIteratorType(nameof(ThingWithComps.GetComps)),
                    p => p.Name.EndsWith(nameof(IEnumerator.Current)) && p.PropertyType.ContainsGenericParameters).GetGetMethod(true),
                typeof(ShieldUtility).FindIteratorMethod(nameof(GetComps_PatternMatch_Inlined)),
                AccessTools.FirstProperty(typeof(ShieldUtility).FindIteratorType(nameof(GetComps_PatternMatch_Inlined)),
                    p => p.Name.EndsWith(nameof(IEnumerator.Current)) && p.PropertyType.ContainsGenericParameters).GetGetMethod(true),
            };
            Log.Message("ShieldUtility: test methods:\n\t" + getShieldMethods.Join(func => func.Method.Name, delimiter: "\n\t"));
            foreach (var methodToInline in methodsToInline)
                MarkForAggressiveInlining(methodToInline);
            Log.Message("ShieldUtility: inlined methods:\n\t" + methodsToInline.Join(method => method.FullDescription(), delimiter: "\n\t"));
        }

        private unsafe static void MarkForAggressiveInlining(MethodBase method)
        {
            var iflags = (ushort*)method.MethodHandle.Value + 1;
            *iflags |= (ushort)MethodImplOptions.AggressiveInlining;
        }

        private static IEnumerable<CodeInstruction> PassthroughTranspiler(IEnumerable<CodeInstruction> instructions) => instructions;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T FirstOrDefault_Inlined<T>(this IEnumerable<T> source)
        {
            //if (source is IList<T> list)
            //    return list.Count > 0 ? list[0] : default;
            foreach (var item in source)
                return item;
            return default;
        }

        private static readonly ConditionalWeakTable<ThingWithComps, CompShield> shieldCache =
            new ConditionalWeakTable<ThingWithComps, CompShield>();
        private static readonly AccessTools.FieldRef<ThingWithComps, List<ThingComp>> ThingWithComps_comps =
            AccessTools.FieldRefAccess<ThingWithComps, List<ThingComp>>("comps");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ThingWithComps GetShieldForLoop(this Pawn_EquipmentTracker eqTracker, Func<ThingWithComps, CompShield> getCompShield)
        {
            var allEquipment = eqTracker.AllEquipmentListForReading;
            for (int i = 0, count = allEquipment.Count; i < count; i++)
            {
                var equipment = allEquipment[i];
                if (getCompShield(equipment) != null)
                    return equipment;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ThingWithComps GetShieldForeachLoop(this Pawn_EquipmentTracker eqTracker, Func<ThingWithComps, CompShield> getCompShield)
        {
            foreach (var equipment in eqTracker.AllEquipmentListForReading)
            {
                if (getCompShield(equipment) != null)
                    return equipment;
            }
            return null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ThingWithComps GetShieldLINQ(this Pawn_EquipmentTracker eqTracker, Func<ThingWithComps, CompShield> getCompShield)
        {
            return eqTracker.AllEquipmentListForReading.FirstOrDefault(equipment => getCompShield(equipment) != null);
        }

        public static ThingWithComps GetShield_Nop1(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Nop);
        }

        // Exact copy of *_Nop1 to help measure noise/variance.
        public static ThingWithComps GetShield_Nop2(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Nop);
        }

        public static ThingWithComps GetShield_Nop_Foreach(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForeachLoop(GetCompShield_Nop);
        }

        public static ThingWithComps GetShield_Nop_LINQ(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldLINQ(GetCompShield_Nop);
        }

        private static CompShield GetCompShield_Nop(this ThingWithComps thing) => null;

        public static ThingWithComps GetShield_Dummy1(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy1);
        }

        public static CompShield GetCompShield_Dummy1(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                if (comps[i] != null)
                    return null;
            }
            return null;
        }

        // Exact copy of *_Dummy1 to help measure noise/variance.
        public static ThingWithComps GetShield_Dummy2(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy2);
        }

        public static CompShield GetCompShield_Dummy2(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                if (comps[i] != null)
                    return null;
            }
            return null;
        }

        public static ThingWithComps GetShield_Dummy_Inner(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy_Inner);
        }

        public static CompShield GetCompShield_Dummy_Inner(this ThingWithComps thing)
        {
            static CompShield Inner(ThingWithComps thing)
            {
                var comps = thing.AllComps;
                for (int i = 0, length = comps.Count; i < length; i++)
                {
                    if (comps[i] != null)
                        return null;
                }
                return null;
            }
            return Inner(thing);
        }

        public static ThingWithComps GetShield_Dummy_Helper(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy_Helper);
        }

        public static CompShield GetCompShield_Dummy_Helper(this ThingWithComps thing)
        {
            return GetCompShield_Dummy_H(thing);
        }

        private static CompShield GetCompShield_Dummy_H(ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                if (comps[i] != null)
                    return null;
            }
            return null;
        }

        public static ThingWithComps GetShield_Dummy_InlinedHelper(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy_InlinedHelper);
        }

        public static CompShield GetCompShield_Dummy_InlinedHelper(this ThingWithComps thing)
        {
            return GetCompShield_Dummy_InlinedH(thing);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static CompShield GetCompShield_Dummy_InlinedH(ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                if (comps[i] != null)
                    return null;
            }
            return null;
        }

#if COMPS_FIELD_PUBLICIZED
        public static ThingWithComps GetShield_Dummy_UnsafeComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy_UnsafeComps);
        }

        public static CompShield GetCompShield_Dummy_UnsafeComps(this ThingWithComps thing)
        {
            var comps = thing.comps;
            if (comps == null)
                return null;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                if (comps[i] != null)
                    return null;
            }
            return null;
        }
#endif

        public static ThingWithComps GetShield_Dummy_FieldRefComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy_FieldRefComps);
        }

        public static CompShield GetCompShield_Dummy_FieldRefComps(this ThingWithComps thing)
        {
            var comps = ThingWithComps_comps(thing);
            if (comps == null)
                return null;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                if (comps[i] != null)
                    return null;
            }
            return null;
        }

        public static ThingWithComps GetShield_Dummy_CheckAllComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Dummy_CheckAllComps);
        }

        public static CompShield GetCompShield_Dummy_CheckAllComps(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            if (comps.Count == 0)
                return null;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                if (comps[i] != null)
                    return null;
            }
            return null;
        }

        public static ThingWithComps GetShield_PatternMatch_LINQ(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldLINQ(GetCompShield_PatternMatch);
        }

        public static ThingWithComps GetShield_PatternMatch_Foreach(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForeachLoop(GetCompShield_PatternMatch);
        }

        public static ThingWithComps GetShield_PatternMatch(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_PatternMatch);
        }

        public static CompShield GetCompShield_PatternMatch(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp is CompShield compShield)
                    return compShield;
            }
            return null;
        }

        public static ThingWithComps GetShield_Isinst(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Isinst);
        }

        public static CompShield GetCompShield_Isinst(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp is CompShield)
                    return (CompShield)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_GetType_MultiTypeof(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType_MultTypeof);
        }

        public static CompShield GetCompShield_GetType_MultTypeof(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == typeof(CompShield))
                    return (CompShield)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_GetType(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType);
        }

        public static CompShield GetCompShield_GetType(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            var type = typeof(CompShield);
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (CompShield)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_GetType_RefEqual(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType_RefEqual);
        }

        public static CompShield GetCompShield_GetType_RefEqual(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            var type = typeof(CompShield);
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if ((object)comp.GetType() == type)
                    return (CompShield)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_GetType_Helper(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType_Helper);
        }

        public static CompShield GetCompShield_GetType_Helper(this ThingWithComps thing)
        {
            return (CompShield)thing.GetComp(typeof(CompShield));
        }

        public static ThingComp GetComp(this ThingWithComps thing, Type type)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_GetType_InlinedHelper(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType_InlinedHelper);
        }

        public static CompShield GetCompShield_GetType_InlinedHelper(this ThingWithComps thing)
        {
            return (CompShield)thing.GetComp(typeof(CompShield));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ThingComp GetComp_Inlined(this ThingWithComps thing, Type type)
        {
            var comps = thing.AllComps;
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return comp;
            }
            return null;
        }

#if COMPS_FIELD_PUBLICIZED
        public static ThingWithComps GetShield_GetType_UnsafeComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType_UnsafeComps);
        }

        public static CompShield GetCompShield_GetType_UnsafeComps(this ThingWithComps thing)
        {
            var comps = thing.comps;
            if (comps == null)
                return null;
            var type = typeof(CompShield);
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (CompShield)comp;
            }
            return null;
        }
#endif

        public static ThingWithComps GetShield_GetType_FieldRefComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType_FieldRefComps);
        }

        public static CompShield GetCompShield_GetType_FieldRefComps(this ThingWithComps thing)
        {
            var comps = ThingWithComps_comps(thing);
            if (comps == null)
                return null;
            var type = typeof(CompShield);
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (CompShield)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_GetType_CheckAllComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetType_CheckAllComps);
        }

        public static CompShield GetCompShield_GetType_CheckAllComps(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            if (comps.Count == 0)
                return null;
            var type = typeof(CompShield);
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (CompShield)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_IsAssignableFrom(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_IsAssignableFrom);
        }

        public static CompShield GetCompShield_IsAssignableFrom(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            var type = typeof(CompShield);
            for (int i = 0, length = comps.Count; i < length; i++)
            {
                var comp = comps[i];
                if (type.IsAssignableFrom(comp.GetType()))
                    return (CompShield)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_PatternMatch_Cache(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_PatternMatch_Cache);
        }

        public static CompShield GetCompShield_PatternMatch_Cache(this ThingWithComps thing)
        {
            static CompShield Inner(ThingWithComps thing)
            {
                var comps = thing.AllComps;
                for (int i = 0, length = comps.Count; i < length; i++)
                {
                    var comp = comps[i];
                    if (comp is CompShield compShield)
                        return compShield;
                }
                return null;
            }
            return shieldCache.GetValue(thing, Inner);
        }

        public static ThingWithComps GetShield_Gen_TryGetComp_Orig(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_TryGetComp_Orig);
        }

        public static CompShield GetCompShield_Gen_TryGetComp_Orig(this ThingWithComps thing)
        {
            return thing.TryGetComp<CompShield>();
        }

        public static ThingWithComps GetShield_Gen_TryGetComp_Copy(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_TryGetComp_Copy);
        }

        public static CompShield GetCompShield_Gen_TryGetComp_Copy(this ThingWithComps thing)
        {
            return thing.TryGetComp_Copy<CompShield>();
        }

        private static T TryGetComp_Copy<T>(this Thing thing) where T : ThingComp
        {
            return thing is ThingWithComps thingWithComps ? thingWithComps.GetComp<T>() : null;
        }

        public static ThingWithComps GetShield_Gen_TryGetComp_Copy_Inlined(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_TryGetComp_Copy_Inlined);
        }

        public static CompShield GetCompShield_Gen_TryGetComp_Copy_Inlined(this ThingWithComps thing)
        {
            return thing.TryGetComp_Copy_Inlined<CompShield>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T TryGetComp_Copy_Inlined<T>(this Thing thing) where T : ThingComp
        {
            return thing is ThingWithComps thingWithComps ? thingWithComps.GetComp<T>() : null;
        }

        public static ThingWithComps GetShield_GetComps_PatternMatch(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetComps_PatternMatch);
        }

        public static CompShield GetCompShield_GetComps_PatternMatch(this ThingWithComps thing)
        {
            var enumerator = thing.GetCompsShield().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        private static IEnumerable<CompShield> GetCompsShield(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                if (comps[i] is CompShield comp)
                    yield return comp;
            }
        }

        public static ThingWithComps GetShield_GetComps_PatternMatch_List(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetComps_PatternMatch_List);
        }

        public static CompShield GetCompShield_GetComps_PatternMatch_List(this ThingWithComps thing)
        {
            var enumerator = thing.GetCompsShield_List().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        private static IEnumerable<CompShield> GetCompsShield_List(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new List<CompShield>(count);
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is CompShield comp)
                    matched.Add(comp);
            }
            return matched;
        }

        public static ThingWithComps GetShield_GetComps_PatternMatch_List_RetType(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetComps_PatternMatch_List_RetType);
        }

        public static CompShield GetCompShield_GetComps_PatternMatch_List_RetType(this ThingWithComps thing)
        {
            var list = thing.GetCompsShield_List_RetType();
            return list.Count > 0 ? list[0] : null;
        }

        private static List<CompShield> GetCompsShield_List_RetType(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new List<CompShield>(count);
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is CompShield comp)
                    matched.Add(comp);
            }
            return matched;
        }

        public static ThingWithComps GetShield_GetComps_PatternMatch_Array(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetComps_PatternMatch_Array);
        }

        public static CompShield GetCompShield_GetComps_PatternMatch_Array(this ThingWithComps thing)
        {
            var enumerator = thing.GetCompsShield_Array().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        private static IEnumerable<CompShield> GetCompsShield_Array(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new CompShield[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is CompShield comp)
                    matched[j++] = comp;
            }
            Array.Resize(ref matched, j);
            return matched;
        }

        public static ThingWithComps GetShield_GetComps_PatternMatch_Array_RetType(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_GetComps_PatternMatch_Array_RetType);
        }

        public static CompShield GetCompShield_GetComps_PatternMatch_Array_RetType(this ThingWithComps thing)
        {
            var array = thing.GetCompsShield_Array_RetType();
            return array.Length > 0 ? array[0] : null;
        }

        private static CompShield[] GetCompsShield_Array_RetType(this ThingWithComps thing)
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new CompShield[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is CompShield comp)
                    matched[j++] = comp;
            }
            Array.Resize(ref matched, j);
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_Orig(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_Orig);
        }

        public static CompShield GetCompShield_Gen_GetComps_Orig(this ThingWithComps thing)
        {
            var enumerator = thing.GetComps<CompShield>().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        public static ThingWithComps GetShield_Gen_GetComps_PatternMatch(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_PatternMatch);
        }

        public static CompShield GetCompShield_Gen_GetComps_PatternMatch(this ThingWithComps thing)
        {
            var enumerator = thing.GetComps_PatternMatch<CompShield>().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        private static IEnumerable<T> GetComps_PatternMatch<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                if (comps[i] is T comp)
                    yield return comp;
            }
        }

        public static ThingWithComps GetShield_Gen_GetComps_PatternMatch_Inlined(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_PatternMatch_Inlined);
        }

        public static CompShield GetCompShield_Gen_GetComps_PatternMatch_Inlined(this ThingWithComps thing)
        {
            var enumerator = thing.GetComps_PatternMatch_Inlined<CompShield>().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IEnumerable<T> GetComps_PatternMatch_Inlined<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                if (comps[i] is T comp)
                    yield return comp;
            }
        }

        public static ThingWithComps GetShield_Gen_GetComps_List(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_List);
        }

        public static CompShield GetCompShield_Gen_GetComps_List(this ThingWithComps thing)
        {
            var enumerator = thing.GetComps_List<CompShield>().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        private static IEnumerable<T> GetComps_List<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched.Add(comp);
            }
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_List_Inlined(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_List_Inlined);
        }

        public static CompShield GetCompShield_Gen_GetComps_List_Inlined(this ThingWithComps thing)
        {
            var enumerator = thing.GetComps_List_Inlined<CompShield>().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IEnumerable<T> GetComps_List_Inlined<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched.Add(comp);
            }
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_List_RetType(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_List_RetType);
        }

        public static CompShield GetCompShield_Gen_GetComps_List_RetType(this ThingWithComps thing)
        {
            var list = thing.GetComps_List_RetType<CompShield>();
            return list.Count > 0 ? list[0] : null;
        }

        private static List<T> GetComps_List_RetType<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched.Add(comp);
            }
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_List_RetType_Inlined(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_List_RetType_Inlined);
        }

        public static CompShield GetCompShield_Gen_GetComps_List_RetType_Inlined(this ThingWithComps thing)
        {
            var list = thing.GetComps_List_RetType_Inlined<CompShield>();
            return list.Count > 0 ? list[0] : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static List<T> GetComps_List_RetType_Inlined<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched.Add(comp);
            }
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_Array(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_Array);
        }

        public static CompShield GetCompShield_Gen_GetComps_Array(this ThingWithComps thing)
        {
            var enumerator = thing.GetComps_Array<CompShield>().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        private static IEnumerable<T> GetComps_Array<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new T[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched[j++] = comp;
            }
            Array.Resize(ref matched, j);
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_Array_Inlined(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_Array_Inlined);
        }

        public static CompShield GetCompShield_Gen_GetComps_Array_Inlined(this ThingWithComps thing)
        {
            var enumerator = thing.GetComps_Array_Inlined<CompShield>().GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static IEnumerable<T> GetComps_Array_Inlined<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new T[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched[j++] = comp;
            }
            Array.Resize(ref matched, j);
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_Array_RetType(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_Array_RetType);
        }

        public static CompShield GetCompShield_Gen_GetComps_Array_RetType(this ThingWithComps thing)
        {
            var array = thing.GetComps_Array_RetType<CompShield>();
            return array.Length > 0 ? array[0] : null;
        }

        private static T[] GetComps_Array_RetType<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new T[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched[j++] = comp;
            }
            Array.Resize(ref matched, j);
            return matched;
        }

        public static ThingWithComps GetShield_Gen_GetComps_Array_RetType_Inlined(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetComps_Array_RetType_Inlined);
        }

        public static CompShield GetCompShield_Gen_GetComps_Array_RetType_Inlined(this ThingWithComps thing)
        {
            var array = thing.GetComps_Array_RetType_Inlined<CompShield>();
            return array.Length > 0 ? array[0] : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T[] GetComps_Array_RetType_Inlined<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var count = comps.Count;
            var matched = new T[count];
            int j = 0;
            for (int i = 0; i < count; i++)
            {
                if (comps[i] is T comp)
                    matched[j++] = comp;
            }
            Array.Resize(ref matched, j);
            return matched;
        }

        public static ThingWithComps GetShield_Gen_Orig(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_Orig);
        }

        public static CompShield GetCompShield_Gen_Orig(this ThingWithComps thing)
        {
            return thing.GetComp<CompShield>();
        }

        public static ThingWithComps GetShield_Gen_PatternMatch(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_PatternMatch);
        }

        public static CompShield GetCompShield_Gen_PatternMatch(this ThingWithComps thing)
        {
            return thing.GetComp_PatternMatch<CompShield>();
        }

        private static T GetComp_PatternMatch<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                if (comps[i] is T comp)
                    return comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_Gen_PatternMatch_Inlined(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_PatternMatch_Inlined);
        }

        public static CompShield GetCompShield_Gen_PatternMatch_Inlined(this ThingWithComps thing)
        {
            return thing.GetComp_PatternMatch_Inlined<CompShield>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T GetComp_PatternMatch_Inlined<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                if (comps[i] is T comp)
                    return comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_Gen_Isinst(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_Isinst);
        }

        public static CompShield GetCompShield_Gen_Isinst(this ThingWithComps thing)
        {
            return thing.GetComp_Isinst<CompShield>();
        }

        private static T GetComp_Isinst<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp is T)
                    return (T)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_Gen_Isinst_ManualCast(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_Isinst_ManualCast);
        }

        public static CompShield GetCompShield_Gen_Isinst_ManualCast(this ThingWithComps thing)
        {
            return (CompShield)thing.GetComp_Isinst_NoCast<CompShield>();
        }

        private static ThingComp GetComp_Isinst_NoCast<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp is T)
                    return comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_Gen_GetType_ManualCast(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetType_ManualCast);
        }

        public static CompShield GetCompShield_Gen_GetType_ManualCast(this ThingWithComps thing)
        {
            return (CompShield)thing.GetComp_GetType_NoCast<CompShield>();
        }

        private static ThingComp GetComp_GetType_NoCast<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var type = typeof(T);
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_Gen_GetType(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetType);
        }

        public static CompShield GetCompShield_Gen_GetType(this ThingWithComps thing)
        {
            return thing.GetComp_GetType<CompShield>();
        }

        private static T GetComp_GetType<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            var type = typeof(T);
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (T)comp;
            }
            return null;
        }

#if COMPS_FIELD_PUBLICIZED
        public static ThingWithComps GetShield_Gen_GetType_UnsafeComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetType_UnsafeComps);
        }

        public static CompShield GetCompShield_Gen_GetType_UnsafeComps(this ThingWithComps thing)
        {
            return thing.GetComp_GetType_UnsafeComps<CompShield>();
        }

        private static T GetComp_GetType_UnsafeComps<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.comps;
            if (comps == null)
                return null;
            var type = typeof(T);
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (T)comp;
            }
            return null;
        }
#endif

        public static ThingWithComps GetShield_Gen_GetType_FieldRefComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetType_FieldRefComps);
        }

        public static CompShield GetCompShield_Gen_GetType_FieldRefComps(this ThingWithComps thing)
        {
            return thing.GetComp_GetType_FieldRefComps<CompShield>();
        }

        private static T GetComp_GetType_FieldRefComps<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = ThingWithComps_comps(thing);
            if (comps == null)
                return null;
            var type = typeof(T);
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (T)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_Gen_GetType_CheckAllComps(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetType_CheckAllComps);
        }

        public static CompShield GetCompShield_Gen_GetType_CheckAllComps(this ThingWithComps thing)
        {
            return thing.GetComp_GetType_CheckAllComps<CompShield>();
        }

        private static T GetComp_GetType_CheckAllComps<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            if (comps.Count == 0)
                return null;
            var type = typeof(T);
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == type)
                    return (T)comp;
            }
            return null;
        }

        public static ThingWithComps GetShield_Gen_GetType_MultTypeof(this Pawn_EquipmentTracker eqTracker)
        {
            return eqTracker.GetShieldForLoop(GetCompShield_Gen_GetType_MultTypeof);
        }

        public static CompShield GetCompShield_Gen_GetType_MultTypeof(this ThingWithComps thing)
        {
            return thing.GetComp_GetType_MultTypeof<CompShield>();
        }

        private static T GetComp_GetType_MultTypeof<T>(this ThingWithComps thing) where T : ThingComp
        {
            var comps = thing.AllComps;
            for (int i = 0, count = comps.Count; i < count; i++)
            {
                var comp = comps[i];
                if (comp.GetType() == typeof(T))
                    return (T)comp;
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
        /// Attempts to get the first shield from the equipment tracker.
        /// </summary>
        /// <param name="eqTracker">Equipment tracker to get shield from.</param>
        /// <returns>Shield if tracker has any or null if there is no shield.</returns>
        public static ThingWithComps GetShield(this Pawn_EquipmentTracker eqTracker)
        {
            var shuffled = getShieldMethods.ToList();
            shuffled.Shuffle();
            shuffled.Add(eqTracker => eqTracker.GetShieldForLoop(GetCompShield));
            ThingWithComps thing = null;
            for (int i = 0, count = getShieldMethods.Count; i < count; i++)
            {
                thing = shuffled[i](eqTracker);
            }
            return thing;
        }
    }
}
