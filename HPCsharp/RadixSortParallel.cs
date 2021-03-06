﻿// TODO: Idea: Since SortRadix2 significantly improves memory access pattern of Radix Sort of User Defined Classes (i.e. array of references with are scattered within the heap)
//       use this improvement (which is about 10X speedup) to speedup the parallel version, as each of the tasks will only get in each other's way during the first pass, and
//       will use the much improved memory access pattern of subsequent passes of the LSD Radix Sort algorithm, hopefully providing parallel acceleration.
// TODO: Set parallelism for Parallel Radix Sort to the number of CPU cores by default.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HPCsharp
{
    static public partial class ParallelAlgorithm
    {
        public static Int32 ThresholdByteCount { get; set; } = 64 * 1024;

        private static uint[][] ByteCountInner(uint[] inArray, Int32 l, Int32 r, int numberOfDigits, int numberOfBins)
        {
            uint[][] countLeft = new uint[numberOfDigits][];
            for (int i = 0; i < numberOfDigits; i++)
                countLeft[i] = new uint[numberOfBins];

            if (l > r)      // zero elements to compare
                return countLeft;
            if ((r - l + 1) <= ThresholdByteCount)
            {
                var digits = new byte[4];
                UInt32ByteUnion union = new UInt32ByteUnion();
                for (int current = l; current <= r; current++)    // Scan the array and count the number of times each digit value appears - i.e. size of each bin
                {
                    union.integer = inArray[current];
                    countLeft[0][union.byte0]++;
                    countLeft[1][union.byte1]++;
                    countLeft[2][union.byte2]++;
                    countLeft[3][union.byte3]++;
                }
                return countLeft;
            }

            int m = (r + l) / 2;

            uint[][] countRight = new uint[numberOfDigits][];
            for (int i = 0; i < numberOfDigits; i++)
                countRight[i] = new uint[numberOfBins];

            Parallel.Invoke(() =>
                {
                    countLeft = ByteCountInner(inArray, l,      m, numberOfDigits, numberOfBins);
                },
                () =>
                {
                    countRight = ByteCountInner(inArray, m + 1, r, numberOfDigits, numberOfBins);
                }
            );
            // Combine left and right results
            for (int i = 0; i < numberOfDigits; i++)
                for(int j = 0; j < numberOfBins; j++)
                    countLeft[i][j] += countRight[i][j];

            return countLeft;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct UInt32ByteUnion
        {
            [FieldOffset(0)]
            public byte byte0;
            [FieldOffset(1)]
            public byte byte1;
            [FieldOffset(2)]
            public byte byte2;
            [FieldOffset(3)]
            public byte byte3;

            [FieldOffset(0)]
            public UInt32 integer;
        }
        /// <summary>
        /// Sort an array of unsigned integers using Radix Sorting algorithm (least significant digit variation)
        /// </summary>
        /// <param name="inputArray"></param>
        /// <returns>array of unsigned integers</returns>
        public static uint[] SortRadixPar(this uint[] inputArray)
        {
            int numberOfBins = 256;
            int numberOfDigits = 4;
            int Log2ofPowerOfTwoRadix = 8;
            int d = 0;
            uint[] outputArray = new uint[inputArray.Length];

            uint[][] count = new uint[numberOfDigits][];
            for (int i = 0; i < numberOfDigits; i++)
                count[i] = new uint[numberOfBins];
            uint[][] startOfBin = new uint[numberOfDigits][];
            for (int i = 0; i < numberOfDigits; i++)
                startOfBin[i] = new uint[numberOfBins];
            bool outputArrayHasResult = false;

            uint bitMask = 255;
            int shiftRightAmount = 0;

            //Stopwatch stopwatch = new Stopwatch();
            //long frequency = Stopwatch.Frequency;
            ////Console.WriteLine("  Timer frequency in ticks per second = {0}", frequency);
            //long nanosecPerTick = (1000L * 1000L * 1000L) / frequency;

            //stopwatch.Restart();
#if false
            var digits = new byte[4];
            UInt32ByteUnion union = new UInt32ByteUnion();
            for (uint current = 0; current < inputArray.Length; current++)    // Scan the array and count the number of times each digit value appears - i.e. size of each bin
            {
                union.integer = inputArray[current];
                count[0][union.byte0]++;
                count[1][union.byte1]++;
                count[2][union.byte2]++;
                count[3][union.byte3]++;
            }
#else
            count = ByteCountInner(inputArray, 0, inputArray.Length - 1, numberOfDigits, numberOfBins);
#endif
            //stopwatch.Stop();
            //double timeForCounting = stopwatch.ElapsedTicks * nanosecPerTick / 1000000000.0;
            //Console.WriteLine("Time for counting: {0}", timeForCounting);

            for (d = 0; d < numberOfDigits; d++)
            {
                startOfBin[d][0] = 0;
                for (uint i = 1; i < numberOfBins; i++)
                    startOfBin[d][i] = startOfBin[d][i - 1] + count[d][i - 1];
            }

            d = 0;
            while (bitMask != 0)    // end processing digits when all the mask bits have been processed and shifted out, leaving no bits set in the bitMask
            {
                //stopwatch.Restart();
                uint[] startOfBinLoc = startOfBin[d];
                for (uint current = 0; current < inputArray.Length; current++)
                {
                    //outputArray[startOfBinLoc[ExtractDigit(inputArray[current], bitMask, shiftRightAmount)]++] = inputArray[current];
                    outputArray[startOfBinLoc[(inputArray[current] & bitMask) >> shiftRightAmount]++] = inputArray[current];
                }
                //stopwatch.Stop();
                //double timeForPermuting = stopwatch.ElapsedTicks * nanosecPerTick / 1000000000.0;
                //Console.WriteLine("Time for permuting: {0}", timeForPermuting);

                bitMask <<= Log2ofPowerOfTwoRadix;
                shiftRightAmount += Log2ofPowerOfTwoRadix;
                outputArrayHasResult = !outputArrayHasResult;
                d++;

                uint[] tmp = inputArray;       // swap input and output arrays
                inputArray = outputArray;
                outputArray = tmp;
            }
            if (outputArrayHasResult)
                for (uint current = 0; current < inputArray.Length; current++)    // copy from output array into the input array
                    inputArray[current] = outputArray[current];

            return inputArray;
        }
    }

    class CustomData
    {
        public uint current;
        public uint r;
        public uint bitMask;
        public int shiftRightAmount;
    }

    static public partial class ParallelAlgorithmExperimental
    {
        /// <summary>
        /// Minimal amount of work to be performed in parallel
        /// </summary>
        public static UInt32 SortRadixParallelWorkQuanta { get; set; } = 8 * 1024;
        ///// <summary>
        ///// Number of tasks that will run in parallel within the Parallel Radix Sort algorithm
        ///// </summary>
        //public static Int32 SortRadixParallelAmountOfParallelism { get; set; } = Environment.ProcessorCount;
        /// <summary>
        /// Sort an array of unsigned integers using Parallel Radix Sorting algorithm (least significant digit variation)
        /// </summary>
        /// <param name="inputArray"></param>
        /// <returns>array of unsigned integers</returns>
        public static uint[] SortRadixPar1(this uint[] inputArray)
        {
            uint numberOfBins = 256;
            int Log2ofPowerOfTwoRadix = 8;
            uint[] outputArray = new uint[inputArray.Length];
            bool outputArrayHasResult = false;

            uint numWorkItems = (uint)inputArray.Length / SortRadixParallelWorkQuanta + 1;

            uint[][] count = new uint[numWorkItems][];          // count        for each parallel work item
            for (int i = 0; i < numWorkItems; i++)
                count[i] = new uint[numberOfBins];
            uint[][] startOfBin = new uint[numWorkItems][];     // start of bin for each parallel work item
            for (int i = 0; i < numWorkItems; i++)
                startOfBin[i] = new uint[numberOfBins];

            // Use TPL ideas from https://docs.microsoft.com/en-us/dotnet/standard/parallel-programming/task-based-asynchronous-programming

            uint bitMask = 255;
            int shiftRightAmount = 0;

            while (bitMask != 0)    // end processing digits when all the mask bits have been processed and shifted out, leaving no bits set in the bitMask
            {
                for (uint r = 0; r < numWorkItems; r++)
                    for (uint c = 0; c < numberOfBins; c++)
                        count[r][c] = 0;
                for (uint current = 0; current < inputArray.Length; current++)    // Scan the array and count the number of times each digit value appears - i.e. size of each bin
                {
                    uint r = current / SortRadixParallelWorkQuanta;
                    count[r][ExtractDigit(inputArray[current], bitMask, shiftRightAmount)]++;
                }
                for (uint c = 0; c < numberOfBins; c++)     // for each column, which is a bin, create startOfBin for each work item, but relative to zero
                {
                    startOfBin[0][c] = 0;
                    for (uint r = 1; r < numWorkItems; r++) // Victor J. Duvanenko https://github.com/DragonSpit/HPCsharp
                        startOfBin[r][c] = (uint)(startOfBin[r - 1][c] + count[r - 1][c]);
                }
                for (uint c = 1; c < numberOfBins; c++)     // adjust each item within each bin by the offset of previous bin and that bins size
                {
                    uint sizeOfPreviouBin = startOfBin[numWorkItems - 1][c - 1] + count[numWorkItems - 1][c - 1];
                    for (uint r = 0; r < numWorkItems; r++)
                        startOfBin[r][c] += sizeOfPreviouBin;
                }

#if false
                for (uint current = 0; current < inputArray.Length; current++)
                {
                    uint r = current / SortRadixParallelWorkQuanta;
                    outputArray[startOfBin[r, ExtractDigit(inputArray[current], bitMask, shiftRightAmount)]++] = inputArray[current];
                }
#else
#if true
                // The last work item may not have the full parallelWorkQuanta of items to process
                Task[] taskArray = new Task[numWorkItems - 1];
                for (uint r = 0; r < numWorkItems - 1; r++)
                {
                    uint current = r * SortRadixParallelWorkQuanta;
                    taskArray[r] = Task.Factory.StartNew((Object obj) => {
                            CustomData data = obj as CustomData;
                            if (data == null)
                                return;
                            uint currIndex = data.current;
                            uint rLoc = data.r;
                            uint[] startOfBinLoc = startOfBin[rLoc];
                            //Console.WriteLine("current = {0}, r = {1}, bitMask = {2}, shiftRightAmount = {3}", currIndex, rLoc, data.bitMask, data.shiftRightAmount);
                            for (uint i = 0; i < SortRadixParallelWorkQuanta; i++)
                            {
                                outputArray[startOfBinLoc[ExtractDigit(inputArray[currIndex], data.bitMask, data.shiftRightAmount)]++] = inputArray[currIndex];
                                currIndex++;
                            }
                        },
                        new CustomData() { current = current, r = r, bitMask = bitMask, shiftRightAmount = shiftRightAmount }
                    );
                }
                Task.WaitAll(taskArray);
#else
                // The last work item may not have the full parallelWorkQuanta of items to process
                for (uint r = 0; r < numWorkItems - 1; r++)
                {
                    uint current = r * SortRadixParallelWorkQuanta;
                    for (uint i = 0; i < SortRadixParallelWorkQuanta; i++)
                    {
                        outputArray[startOfBin[r, ExtractDigit(inputArray[current], bitMask, shiftRightAmount)]++] = inputArray[current];
                        current++;
                    }
                }
#endif
                // The last iteration, which may not have the full parallelWorkQuanta of items to process
                uint currentLast = (numWorkItems - 1) * SortRadixParallelWorkQuanta;
                uint numItems = (uint)inputArray.Length % SortRadixParallelWorkQuanta;
                for (uint i = 0; i < numItems; i++)
                {
                    outputArray[startOfBin[(numWorkItems - 1)][ExtractDigit(inputArray[currentLast], bitMask, shiftRightAmount)]++] = inputArray[currentLast];
                    currentLast++;
                }
#endif

                bitMask <<= Log2ofPowerOfTwoRadix;
                shiftRightAmount += Log2ofPowerOfTwoRadix;
                outputArrayHasResult = !outputArrayHasResult;

                uint[] tmp = inputArray;       // swap input and output arrays
                inputArray = outputArray;
                outputArray = tmp;
            }
            if (outputArrayHasResult)
                for (uint current = 0; current < inputArray.Length; current++)    // copy from output array into the input array
                    inputArray[current] = outputArray[current];

            return inputArray;
        }
        private static void DoRadixSortLsdParallel(uint current, uint[] outputArray, uint[,] startOfBin, uint r, uint[] inputArray, uint bitMask, int shiftRightAmount)
        {
            for (uint i = 0; i < SortRadixParallelWorkQuanta; i++)
            {
                outputArray[startOfBin[r, ExtractDigit(inputArray[current], bitMask, shiftRightAmount)]++] = inputArray[current];
                current++;
            }
        }
        private static UInt32 ExtractDigit(UInt32 value, UInt32 bitMask, int shiftRightAmount)
        {
            return (value & bitMask) >> shiftRightAmount;	// extract the digit we are sorting based on
        }
        private static UInt32 ExtractDigit(UInt64 value, UInt64 bitMask, int shiftRightAmount)
        {
            return (UInt32)((value & bitMask) >> shiftRightAmount);	// extract the digit we are sorting based on
        }
    }
}
