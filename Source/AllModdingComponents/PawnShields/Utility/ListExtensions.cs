using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PawnShields
{
    public static class ListExtensions
    {
        public static List<T> AsList<T>(this IEnumerable<T> enumerable) =>
            enumerable is List<T> list ? list : new List<T>(enumerable);

        public static void ReplaceRange<T>(this List<T> list, int startIndex, int count, IEnumerable<T> newItems)
        {
            var enumerator = newItems.GetEnumerator();
            var index = startIndex;
            var endIndex = Math.Min(startIndex + count, list.Count);
            while (index < endIndex)
            {
                if (!enumerator.MoveNext())
                {
                    list.RemoveRange(index, endIndex - index);
                    return;
                }
                list[index] = enumerator.Current;
                index++;
            }
            if (enumerator.MoveNext())
            {
                var remainingItems = new List<T> { enumerator.Current };
                while (enumerator.MoveNext())
                {
                    remainingItems.Add(enumerator.Current);
                }
                list.InsertRange(index, remainingItems);
            }
        }

        public static List<T> PopAll<T>(this ICollection<T> collection)
        {
            var list = new List<T>(collection);
            collection.Clear();
            return list;
        }

        public static int FindSequenceIndex<T>(this List<T> list, params Predicate<T>[] sequenceMatches)
        {
            return list.FindSequenceIndex(0, list.Count, sequenceMatches);
        }

        public static int FindSequenceIndex<T>(this List<T> list, int startIndex, params Predicate<T>[] sequenceMatches)
        {
            return list.FindSequenceIndex(startIndex, list.Count - startIndex, sequenceMatches);
        }

        public static int FindSequenceIndex<T>(this List<T> list, int startIndex, int count, params Predicate<T>[] sequenceMatches)
        {
            if (sequenceMatches is null)
                throw new ArgumentNullException(nameof(sequenceMatches));
            if (sequenceMatches.Length == 0)
                throw new ArgumentException($"sequenceMatches must not be empty");
            if (count - sequenceMatches.Length < 0)
                return -1;
            count -= sequenceMatches.Length - 1;
            var index = list.FindIndex(startIndex, count, sequenceMatches[0]);
            while (index != -1)
            {
                var allMatched = true;
                for (var matchIndex = 1; matchIndex < sequenceMatches.Length; matchIndex++)
                {
                    if (!sequenceMatches[matchIndex](list[index + matchIndex]))
                    {
                        allMatched = false;
                        break;
                    }
                }
                if (allMatched)
                    break;
                startIndex++;
                count--;
                index = list.FindIndex(startIndex, count, sequenceMatches[0]);
            }
            return index;
        }
    }

    // TODO: Move this class into own file or rename this file to MiscExtensions
    public static class TypeExtensions
    {
        // The MoveNext method may be either public or non-public, depending on the compiler.
        public static Type FindIteratorType(this Type type, string parentMethodName, Func<Type, bool> predicate = null)
        {
            // Iterator code is in a compiler-generated non-public nested class that implements IEnumerable.
            // In RW 1.1+ assemblies and modern VS-compiled assemblies, the nested class's name starts with "<{parentMethodName}>".
            foreach (var innerType in type.GetNestedTypes(BindingFlags.NonPublic))
            {
                if (innerType.IsDefined(typeof(CompilerGeneratedAttribute)) &&
                    typeof(IEnumerator).IsAssignableFrom(innerType) &&
                    innerType.Name.StartsWith("<" + parentMethodName + ">") &&
                    (predicate is null || predicate(innerType)))
                {
                    return innerType;
                }
            }
            throw new ArgumentException($"Could not find any iterator type for parent type {type} and method {parentMethodName}" +
                " that satisfied given predicate");
        }

        private const BindingFlags moveNextMethodBindingFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        public static MethodInfo FindIteratorMethod(this Type type, string parentMethodName, Func<Type, bool> predicate = null)
        {
            return type.FindIteratorType(parentMethodName, predicate).GetMethod(nameof(IEnumerator.MoveNext), moveNextMethodBindingFlags);
        }
    }
}
