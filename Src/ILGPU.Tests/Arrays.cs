// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                         Copyright (c) 0-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: Arrays.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

        private static readonly int[,,,] StaticData = new int[,,,]
        {
            { { { 0, 1, 2, 3 } } },
            { { { 3, 4, 5, 6 } } },
            { { { 7, 8, 9, 10 } } },
        };

        private static readonly ImmutableArray<int> StaticImmutableData =
            ImmutableArray.Create(
                1, 2, 3, 4);

        internal static void MultiDimStaticArrayKernel(
            Index1D index,
            ArrayView<int> data)
        {
            data[index] = StaticData[0, 0, 0, index + 3];
            data[index + 1] = StaticData[1, 0, 0, index + 3];
            data[index + 2] = StaticData[2, 0, 0, index + 3];
        }

        [Fact]
        [KernelMethod(nameof(MultiDimStaticArrayKernel))]
        public void MultiDimStaticArray()
        {
            using var buffer = Accelerator.Allocate1D<int>(3);
            Execute(1, buffer.AsContiguous());

            var expected = new int[] { 3, 6, 10 };
            Verify(buffer.View, expected);
        }

        internal static void StaticImmutableArrayKernel(
            Index1D index,
            ArrayView<int> data)
        {
            data[index] = StaticImmutableData[index + 3];
        }

        [Fact]
        [KernelMethod(nameof(StaticImmutableArrayKernel))]
        public void StaticImmutableArray()
        {
            using var buffer = Accelerator.Allocate1D<int>(1);
            Execute(1, buffer.AsContiguous());

            var expected = new int[] { 4 };
            Verify(buffer.View, expected);
        }

        internal static void StaticInlineArrayKernel(
            Index1D index,
            ArrayView<int> data)
        {
            var staticInlineArray = new int[] { 1, 2, 3, 4 };
            data[index] = staticInlineArray[index + 3];
        }

        [Fact]
        [KernelMethod(nameof(StaticInlineArrayKernel))]
        public void StaticInlineArray()
        {
            using var buffer = Accelerator.Allocate1D<int>(1);
            Execute(1, buffer.AsContiguous());

            var expected = new int[] { 4 };
            Verify(buffer.View, expected);
        }

        internal static void StaticInlineStructureArrayKernel(
            Index1D index,
            ArrayView<int> data)
        {
            var staticInlineArray = new PairStruct<int, int>[]
            {
                new PairStruct<int, int>(1, 2),
                new PairStruct<int, int>(3, 4),
            };
            data[index] =
                staticInlineArray[0].Val0 +
                staticInlineArray[1].Val1;
        }

        [Fact]
        [KernelMethod(nameof(StaticInlineStructureArrayKernel))]
        public void StaticInlineStructureArray()
        {
            using var buffer = Accelerator.Allocate1D<int>(1);
            Execute(1, buffer.AsContiguous());

            var expected = new int[] { 5 };
            Verify(buffer.View, expected);
        }

        internal static void ConditionalArrayFoldingKernel(
            Index1D index,
            ArrayView<int> buffer)
        {
            int[] values = new[] { 0, 1 };

            if (index == values[0])
                buffer[index] = 42;
            else
                buffer[index] = 24;
        }

        [Fact]
        [KernelMethod(nameof(ConditionalArrayFoldingKernel))]
        public void ConditionalArrayFolding()
        {
            using var buffer = Accelerator.Allocate1D<int>(4);
            Execute(buffer.IntExtent, buffer.AsContiguous());

            var expected = new int[] { 42, 24, 24, 24 };
            Verify(buffer.View, expected);
        }

        internal static void ConditionalArrayPartialFoldingKernel(
            Index1D index,
            ArrayView<int> buffer,
            int constant)
        {
            int[] values = new[] { 0, 1 };

            if (index == values[1] & constant == values[0])
                buffer[index] = 42;
            else
                buffer[index] = 24;
        }

        [InlineData(0)]
        [InlineData(1)]
        [Theory]
        [KernelMethod(nameof(ConditionalArrayPartialFoldingKernel))]
        public void ConditionalArrayPartialFolding(int constantValue)
        {
            using var buffer = Accelerator.Allocate1D<int>(4);
            Execute(4, buffer.AsContiguous(), constantValue);

            var expected = new int[] { 24, 42, 24, 24 };
            expected[constantValue] = 24;
            Verify(buffer.View, expected);
        }
    }
}

#pragma warning restore CA1814 // Prefer jagged arrays over multidimensional
#pragma warning restore xUnit1026 // Theory methods should use all of their parameters
