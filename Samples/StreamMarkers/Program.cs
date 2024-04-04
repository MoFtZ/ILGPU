// ---------------------------------------------------------------------------------------
//                                    ILGPU Samples
//                        Copyright (c) 2021-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: Program.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU;
using ILGPU.Runtime;
using System;
using System.Linq;

namespace StreamMarkers
{
    class Program
    {
        static void MyKernelA(Index1D index, int constant)
        {
            Interop.WriteLine("A-{0}", constant);
        }
        static void MyKernelB(Index1D index, int constant)
        {
            Interop.WriteLine("B-{0}", constant);
        }
        static void MyKernelC(Index1D index, int constant)
        {
            Interop.WriteLine("C-{0}", constant);
        }

        const int KernelWorkload = 1;
        const int NumIterations = 8;

        static void Main()
        {
            // Create main context
            using var context = Context.Create(builder =>
            {
                builder.Default().DebugConfig(
                    enableIOOperations: true,
                    forceDebuggingOfOptimizedKernels: true);
            });

            foreach (var device in context)
            {
                using var accelerator = device.CreateAccelerator(context);
                Console.WriteLine($"Performing operations on {accelerator}");

                var kernelA = accelerator.LoadAutoGroupedKernel<Index1D, int>(MyKernelA);
                var kernelB = accelerator.LoadAutoGroupedKernel<Index1D, int>(MyKernelB);
                var kernelC = accelerator.LoadAutoGroupedKernel<Index1D, int>(MyKernelC);

                // Create several independant streams of work.
                var streamA = Enumerable.Range(0, NumIterations).Select(x => accelerator.CreateStream()).ToArray();
                var streamB = Enumerable.Range(0, NumIterations).Select(x => accelerator.CreateStream()).ToArray();
                var streamC = Enumerable.Range(0, NumIterations).Select(x => accelerator.CreateStream()).ToArray();

                // Schedules work for Kernel A, and records the position in the stream.
                var markers = Enumerable.Range(0, NumIterations).Select(x => accelerator.CreateStreamMarker()).ToArray();

                for (int i = 0; i < NumIterations; i++)
                {
                    kernelA(streamA[i], KernelWorkload, i);
                    markers[i].Record(streamA[i]);
                }

                // Schedules work for Kernel B on the same stream as Kernel A.
                //
                // Schedules work for Kernel C, on the same stream as Kernel B, only after
                // all other streams have completed Kernel A.
                for (int i = 0; i < NumIterations; i++)
                {
                    kernelB(streamB[i], KernelWorkload, i);

                    // Waits for all the marked streams to have completed Kernel A.
                    // Without this, the work for Kernel C could start before Kernel B.
                    for (int j = 0; j < NumIterations; j++)
                    {
                        if (j != i)
                            streamC[i].WaitForStreamMarker(markers[j]);
                    }

                    kernelC(streamC[i], KernelWorkload, i);
                }

                for (int i = 0; i < NumIterations; i++)
                    streamC[i].Synchronize();

                // Cleanup
                accelerator.Synchronize();

                for (int i = 0; i < NumIterations; i++)
                {
                    markers[i].Dispose();
                    streamA[i].Dispose();
                    streamB[i].Dispose();
                    streamC[i].Dispose();
                }
            }
        }
    }
}
