﻿using ILGPU.Runtime;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;
using Xunit.Abstractions;

namespace ILGPU.Tests
{
    public abstract class SharedMemory : TestBase
    {
        protected SharedMemory(ITestOutputHelper output, TestContext testContext)
            : base(output, testContext)
        { }

        internal static void SharedMemoryVariableKernel(ArrayView<int> data)
        {
            ref var sharedMemory = ref ILGPU.SharedMemory.Allocate<int>();
            if (Group.IsFirstThread)
                sharedMemory = 0;
            Group.Barrier();

            Atomic.Add(ref sharedMemory, 1);
            Group.Barrier();

            var idx = Grid.GlobalIndex.X;
            data[idx] = sharedMemory;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [KernelMethod(nameof(SharedMemoryVariableKernel))]
        public void SharedMemoryVariable(int groupMultiplier)
        {
            for (int i = 1; i < Accelerator.MaxNumThreadsPerGroup; i <<= 1)
            {
                using var buffer = Accelerator.Allocate<int>(i * groupMultiplier);
                var index = new KernelConfig(groupMultiplier, i);
                Execute(index, buffer.View);

                var expected = Enumerable.Repeat(i, (int)buffer.Length).ToArray();
                Verify(buffer, expected);
            }
        }

        internal static void SharedMemoryArrayKernel(ArrayView<int> data)
        {
            var sharedMemory = ILGPU.SharedMemory.Allocate<int>(2);
            sharedMemory[Group.IdxX] = Group.IdxX;
            Group.Barrier();

            var idx = Grid.GlobalIndex.X;
            data[idx] = sharedMemory[(Group.IdxX + 1) % Group.DimX];
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(32)]
        [KernelMethod(nameof(SharedMemoryArrayKernel))]
        public void SharedMemoryArray(int groupMultiplier)
        {
            using var buffer = Accelerator.Allocate<int>(2 * groupMultiplier);
            var index = new KernelConfig(groupMultiplier, 2);
            Execute(index, buffer.View);

            var expected = new int[buffer.Length];
            for (int i = 0; i < buffer.Length; i += 2)
            {
                expected[i] = 1;
                expected[i + 1] = 0;
            }

            Verify(buffer, expected);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int AllocateSharedMemoryNested()
        {
            var sharedMemory = ILGPU.SharedMemory.Allocate<int>(1024);
            sharedMemory[Group.IdxX] = Group.IdxX;
            Group.Barrier();

            return sharedMemory[(Group.IdxX + 1) % Group.DimX];
        }

        internal static void SharedMemoryNestedKernel(ArrayView<int> data)
        {
            var idx = Grid.GlobalIndex.X;
            data[idx] = AllocateSharedMemoryNested();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(32)]
        [KernelMethod(nameof(SharedMemoryNestedKernel))]
        public void SharedMemoryNested(int groupMultiplier)
        {
            using var buffer = Accelerator.Allocate<int>(2 * groupMultiplier);
            var index = new KernelConfig(groupMultiplier, 2);
            Execute(index, buffer.View);

            var expected = new int[buffer.Length];
            for (int i = 0; i < buffer.Length; i += 2)
            {
                expected[i] = 1;
                expected[i + 1] = 0;
            }

            Verify(buffer, expected);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int DynamicSharedMemoryNested()
        {
            var dynamicMemory = ILGPU.SharedMemory.GetDynamic<byte>();
            dynamicMemory[Group.IdxX] = (byte)Group.IdxX;
            Group.Barrier();

            return dynamicMemory[(Group.IdxX + 1) % Group.DimX];
        }

        internal static void DynamicSharedMemoryKernel(ArrayView<int> data)
        {
            var dynamicMemory = ILGPU.SharedMemory.GetDynamic<int>();
            dynamicMemory[Group.IdxX] = Group.IdxX;
            Group.Barrier();
            int value = dynamicMemory[(Group.IdxX + 1) % Group.DimX];

            var idx = Grid.GlobalIndex.X;
            data[idx] = value + DynamicSharedMemoryNested();
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(32)]
        [KernelMethod(nameof(DynamicSharedMemoryKernel))]
        public void DynamicSharedMemory(int groupMultiplier)
        {
            using var buffer = Accelerator.Allocate<int>(2 * groupMultiplier);
            var config = SharedMemoryConfig.RequestDynamic<int>(1024);
            var index = new KernelConfig(groupMultiplier, 2, config);
            Execute(index, buffer.View);

            var expected = new int[buffer.Length];
            for (int i = 0; i < buffer.Length; i += 2)
            {
                expected[i] = 2;
                expected[i + 1] = 0;
            }

            Verify(buffer, expected);
        }

        private static void DynamicSharedMemoryLengthKernel<T>(ArrayView<long> output)
            where T : unmanaged
        {
            var dynamicMemory = ILGPU.SharedMemory.GetDynamic<T>();
            output[0] = dynamicMemory.Length;
            output[1] = dynamicMemory.IntLength;
            output[2] = dynamicMemory.LengthInBytes;
        }

        [Theory]
        [InlineData(default(byte), 11)]
        [InlineData(default(int), 13)]
        [InlineData(default(long), 15)]
        [InlineData(default(float), 17)]
        [InlineData(default(double), 19)]
        [KernelMethod(nameof(DynamicSharedMemoryLengthKernel))]
        [SuppressMessage(
            "Usage",
            "xUnit1026:Theory methods should use all of their parameters",
            Justification = "Required to infer generic type argument")]
        public void DynamicSharedMemoryLength<T>(T elementType, int length)
            where T : unmanaged
        {
            var expectedSizeInBytes = length * Interop.SizeOf<T>();
            var expected = new long[] { length, length, expectedSizeInBytes };

            using var output = Accelerator.AllocateZero<long>(expected.Length);
            var config = SharedMemoryConfig.RequestDynamic<T>(length);
            var index = new KernelConfig(1, 2, config);
            Execute<KernelConfig, T>(index, output.View);
            Verify(output, expected);
        }

        internal static void ImplicitSharedMemoryKernel(Index1 index, ArrayView<int> data)
        {
            data[index] = AllocateSharedMemoryNested();
        }

        [Fact]
        [KernelMethod(nameof(ImplicitSharedMemoryKernel))]
        public void ImplicitlyGroupedSharedMemory()
        {
            using var buffer = Accelerator.Allocate<int>(10);
            Assert.Throws<InternalCompilerException>(() =>
                Execute(buffer.Length, buffer.View));
        }
    }
}
