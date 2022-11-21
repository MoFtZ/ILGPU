// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2022 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXIntrinsics.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.AtomicOperations;
using ILGPU.IR;
using ILGPU.IR.Intrinsics;
using ILGPU.IR.Values;
using ILGPU.Runtime.Cuda;
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.PTX
{
    /// <summary>
    /// Implements and initializes PTX intrinsics.
    /// </summary>
    static partial class PTXIntrinsics
    {
        #region Specializers

        /// <summary>
        /// The PTXIntrinsics type.
        /// </summary>
        private static readonly Type PTXIntrinsicsType = typeof(PTXIntrinsics);

        /// <summary>
        /// The Half implementation type.
        /// </summary>
        private static readonly Type HalfType = typeof(HalfExtensions);

        /// <summary>
        /// Creates a new PTX intrinsic.
        /// </summary>
        /// <param name="name">The name of the intrinsic.</param>
        /// <param name="mode">The implementation mode.</param>
        /// <param name="minArchitecture">The minimum architecture.</param>
        /// <param name="maxArchitecture">The maximum architecture.</param>
        /// <returns>The created intrinsic.</returns>
        private static PTXIntrinsic CreateIntrinsic(
            string name,
            IntrinsicImplementationMode mode,
            CudaArchitecture? minArchitecture,
            CudaArchitecture maxArchitecture) =>
            new PTXIntrinsic(
                PTXIntrinsicsType,
                name,
                mode,
                minArchitecture,
                maxArchitecture);

        /// <summary>
        /// Creates a new PTX intrinsic.
        /// </summary>
        /// <param name="name">The name of the intrinsic.</param>
        /// <param name="mode">The implementation mode.</param>
        /// <returns>The created intrinsic.</returns>
        private static PTXIntrinsic CreateIntrinsic(
            string name,
            IntrinsicImplementationMode mode) =>
            new PTXIntrinsic(PTXIntrinsicsType, name, mode);

        /// <summary>
        /// Creates a new FP16 intrinsic.
        /// </summary>
        /// <param name="name">The name of the intrinsic.</param>
        /// <param name="maxArchitecture">The maximum PTX architecture.</param>
        /// <returns>The created intrinsic.</returns>
        private static PTXIntrinsic CreateFP16Intrinsic(
            string name,
            CudaArchitecture? maxArchitecture) =>
            maxArchitecture.HasValue
            ? new PTXIntrinsic(
                HalfType,
                name,
                IntrinsicImplementationMode.Redirect,
                null,
                maxArchitecture.Value)
            : new PTXIntrinsic(HalfType, name, IntrinsicImplementationMode.Redirect);

        /// <summary>
        /// Registers all PTX intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        public static void Register(IntrinsicImplementationManager manager)
        {
            RegisterAtomics(manager);
            RegisterBroadcasts(manager);
            RegisterWarpShuffles(manager);
            RegisterFP16(manager);
            RegisterBitFunctions(manager);
#if NET7_0_OR_GREATER
            RegisterUnsafeFunctions(manager);
#endif
        }

        #endregion

        #region Atomics

        /// <summary>
        /// Registers all atomic intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterAtomics(IntrinsicImplementationManager manager) =>
            manager.RegisterGenericAtomic(
                AtomicKind.Add,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(AtomicAddF64),
                    IntrinsicImplementationMode.Redirect,
                    null,
                    CudaArchitecture.SM_60));

        /// <summary>
        /// Represents an atomic compare-exchange operation of type double.
        /// </summary>
        private readonly struct AddDouble : IAtomicOperation<double>
        {
            public double Operation(double current, double value) => current + value;
        }

        /// <summary>
        /// A software implementation for atomic adds on 64-bit floats.
        /// </summary>
        /// <param name="target">The target address.</param>
        /// <param name="value">The value to add.</param>
        private static double AtomicAddF64(ref double target, double value) =>
            Atomic.MakeAtomic(
                ref target,
                value,
                new AddDouble(),
                new CompareExchangeDouble());

        #endregion

        #region Broadcasts

        /// <summary>
        /// Registers all broadcast intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterBroadcasts(
            IntrinsicImplementationManager manager)
        {
            manager.RegisterBroadcast(
                BroadcastKind.GroupLevel,
                CreateIntrinsic(
                    nameof(GroupBroadcast),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterBroadcast(
                BroadcastKind.WarpLevel,
                CreateIntrinsic(
                    nameof(WarpBroadcast),
                    IntrinsicImplementationMode.Redirect));
        }

        /// <summary>
        /// Implements a single group-broadcast operation.
        /// </summary>
        /// <typeparam name="T">The type to broadcast.</typeparam>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T GroupBroadcast<T>(T value, int groupIndex)
            where T : unmanaged
        {
            ref var sharedMemory = ref SharedMemory.Allocate<T>();
            if (Group.LinearIndex == groupIndex)
                sharedMemory = value;
            Group.Barrier();

            return sharedMemory;
        }

        /// <summary>
        /// Wraps a single warp-broadcast operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static T WarpBroadcast<T>(T value, int laneIndex)
            where T : unmanaged =>
            Warp.Shuffle(value, laneIndex);

        #endregion

        #region Bit Functions

        /// <summary>
        /// Registers all unary bit intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterBitFunctions(
            IntrinsicImplementationManager manager)
        {
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CTZ,
                BasicValueType.Int32,
                CreateIntrinsic(
                    nameof(TrailingZeroCountI32),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CTZ,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(TrailingZeroCountI64),
                    IntrinsicImplementationMode.Redirect));
        }

        /// <summary>
        /// Wraps a CTZ operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TrailingZeroCountI32(int value) =>
            IntrinsicMath.BitOperations.TrailingZeroCount(value);

        /// <summary>
        /// Wraps a CTZ operations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int TrailingZeroCountI64(long value) =>
            IntrinsicMath.BitOperations.TrailingZeroCount(value);

        #endregion

        #region Unsafe Functions

#if NET7_0_OR_GREATER

        /// <summary>
        /// Registers all Unsafe intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterUnsafeFunctions(
            IntrinsicImplementationManager manager)
        {
            // List of conversions, of equal size.
            (Type From, Type To)[] combinations = new[]
                {
                    (typeof(double), typeof(long)),
                    (typeof(double), typeof(ulong)),
                    (typeof(long), typeof(ulong)),

                    (typeof(float), typeof(int)),
                    (typeof(float), typeof(uint)),
                    (typeof(int), typeof(uint)),

                    (typeof(Half), typeof(short)),
                    (typeof(Half), typeof(ushort)),
                    (typeof(short), typeof(ushort)),
                };

            // There are multiple declarations of the As method, so we have to find the
            // generic method with two generic arguments.
            var baseMethod = typeof(Unsafe)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m =>
                    nameof(Unsafe.As).Equals(
                        m.Name,
                        StringComparison.OrdinalIgnoreCase))
                .Where(m => m.IsGenericMethod && m.GetGenericArguments().Length == 2)
                .Single();

            // Register the replacements for Unsafe.As methods.
            void registerUnsafeMethod(MethodInfo method) =>
                manager.RegisterMethod(
                    method,
                    new PTXIntrinsic(
                        typeof(PTXIntrinsics),
                        nameof(PTXIntrinsics.GenerateUnsafeAsIntrinsic),
                        IntrinsicImplementationMode.GenerateCode));

            foreach (var (fromType, toType) in combinations)
            {
                var method = baseMethod.MakeGenericMethod(new[] { fromType, toType });
                registerUnsafeMethod(method);

                var inverse = baseMethod.MakeGenericMethod(new[] { toType, fromType });
                registerUnsafeMethod(inverse);
            }
        }

        /// <summary>
        /// Generates intrinsic implementation for Unsafe.As.
        /// </summary>
        /// <param name="backend">The current backend.</param>
        /// <param name="codeGenerator">The code generator.</param>
        /// <param name="value">The value to generate code for.</param>
        private static void GenerateUnsafeAsIntrinsic(
            PTXBackend backend,
            PTXCodeGenerator codeGenerator,
            Value value)
        {
            var methodCall = value as MethodCall;
            var argument = methodCall.Nodes[0];
            var sourceRegister = codeGenerator.LoadHardware(argument);
            codeGenerator.Bind(value, sourceRegister);
        }

#endif

        #endregion
    }
}
