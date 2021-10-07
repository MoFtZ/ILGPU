// ---------------------------------------------------------------------------------------
//                                    ILGPU Samples
//                        Copyright (c) 2017-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: Program.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

            using var buffer = accelerator.Allocate1D<int>(1024);
            // Launch buffer.Length many threads and pass a view to buffer.
            // You can also use kernel.Launch; however, the generic launch method involves boxing.
            launcher(
                accelerator.DefaultStream,
                (int)buffer.Length,
                buffer.View,
                42);

            // Reads data from the GPU buffer into a new CPU array.
            // Implicitly calls accelerator.DefaultStream.Synchronize() to ensure
            // that the kernel and memory copy are completed first.
            var data = buffer.GetAsArray1D();
            for (int i = 0, e = data.Length; i < e; ++i)
            {
                if (data[i] != 42 + i)
                    Console.WriteLine($"Error at element location {i}: {data[i]} found");
            }
        }

        /// <summary>
        /// Launches a simple 1D kernel using implicit and auto-grouping functionality.
        /// This sample demonstrates the creation of launcher delegates in order to avoid boxing.
        /// </summary>
        static void Main()
        {
            // Create main context
            using var context = Context.CreateDefault();

            // For each available device...
            foreach (var device in context)
            {
                // Create accelerator for the given device
                using var accelerator = device.CreateAccelerator(context);
                Console.WriteLine($"Performing operations on {accelerator}");

                // Compiles and launches an implicitly-grouped kernel with an automatically
                // determined group size. The latter is determined either by ILGPU or
                // the GPU driver. This is the most convenient way to launch kernels using ILGPU.
                CompileAndLaunchAutoGroupedKernel(accelerator);

                // Compiles and launches an implicitly-grouped kernel with a custom group
                // size. Note that a group size less than the warp size can cause
                // dramatic performance decreases since many lanes of a warp might remain
                // unused.
                CompileAndLaunchImplicitlyGroupedKernel(accelerator, accelerator.WarpSize);

                // Compiles and launches an explicitly-grouped kernel with a custom group
                // size.
                CompileAndLaunchKernel(accelerator, accelerator.WarpSize);
            }
        }
    }
}
