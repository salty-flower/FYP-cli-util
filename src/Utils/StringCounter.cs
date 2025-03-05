using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace DataCollection.Utils;

public static class StringExtensions
{
    // Threshold for parallel processing (can be tuned based on benchmarks)
    private const int ParallelThreshold = 100_000;

    // Threshold for using stack allocation
    private const int StackAllocThreshold = 256;

    public static int CountSubstring(this string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            return 0;

        if (value.Length > text.Length)
            return 0;

        // For very short strings, use the original simple approach
        if (text.Length < 1000)
            return CountSubstringSimple(text, value);

        // For longer strings, use parallelization if beneficial
        if (text.Length >= ParallelThreshold)
            return CountSubstringParallel(text, value);

        // For medium-length strings, use the optimized single-threaded approach
        return CountSubstringOptimized(text, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountSubstringSimple(string text, string value)
    {
        int count = 0,
            minIndex = text.IndexOf(value, 0);
        while (minIndex != -1)
        {
            minIndex = text.IndexOf(value, minIndex + value.Length);
            count++;
        }
        return count;
    }

    private static int CountSubstringOptimized(string text, string value)
    {
        // Use stack allocation for small arrays
        Span<int> counts = stackalloc int[1];

        // Create a reference to avoid bounds checking
        ref char textStart = ref MemoryMarshal.GetReference(text.AsSpan());
        ref char valueStart = ref MemoryMarshal.GetReference(value.AsSpan());

        int textLength = text.Length;
        int valueLength = value.Length;

        for (int i = 0; i <= textLength - valueLength; i++)
        {
            if (CompareStrings(ref Unsafe.Add(ref textStart, i), ref valueStart, valueLength))
            {
                counts[0]++;
            }
        }

        return counts[0];
    }

    private static int CountSubstringParallel(string text, string value)
    {
        // Calculate optimal chunk size based on CPU cores
        int chunkSize = Math.Max(ParallelThreshold / Environment.ProcessorCount, value.Length * 2);
        int totalCount = 0;

        Parallel.ForEach(
            Partitioner.Create(0, text.Length - value.Length + 1, chunkSize),
            () => 0, // Thread local initial state
            (range, _, threadStart) =>
            {
                int localCount = 0;
                ref char textStart = ref MemoryMarshal.GetReference(text.AsSpan());
                ref char valueStart = ref MemoryMarshal.GetReference(value.AsSpan());

                for (int i = range.Item1; i < range.Item2; i++)
                {
                    if (
                        CompareStrings(
                            ref Unsafe.Add(ref textStart, i),
                            ref valueStart,
                            value.Length
                        )
                    )
                    {
                        localCount++;
                    }
                }
                return localCount;
            },
            localCount => Interlocked.Add(ref totalCount, localCount) // Final reducer
        );

        return totalCount;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool CompareStrings(ref char first, ref char second, int length)
    {
        // Use SIMD-optimized comparison when available
        if (Vector.IsHardwareAccelerated && length >= Vector<short>.Count)
        {
            while (length >= Vector<short>.Count)
            {
                var firstVec = Unsafe.ReadUnaligned<Vector<short>>(
                    ref Unsafe.As<char, byte>(ref first)
                );
                var secondVec = Unsafe.ReadUnaligned<Vector<short>>(
                    ref Unsafe.As<char, byte>(ref second)
                );

                if (!Vector.EqualsAll(firstVec, secondVec))
                    return false;

                first = ref Unsafe.Add(ref first, Vector<short>.Count);
                second = ref Unsafe.Add(ref second, Vector<short>.Count);
                length -= Vector<short>.Count;
            }
        }

        // Handle remaining characters
        while (length > 0)
        {
            if (first != second)
                return false;

            first = ref Unsafe.Add(ref first, 1);
            second = ref Unsafe.Add(ref second, 1);
            length--;
        }

        return true;
    }
}
