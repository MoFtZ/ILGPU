﻿// -----------------------------------------------------------------------------
//                                    ILGPU
//                     Copyright (c) 2016-2019 Marcel Koester
//                                www.ilgpu.net
//
// File: VariableAllocator.cs
//
// This file is part of ILGPU and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details
// -----------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Types;
using System;
using System.Collections.Generic;

namespace ILGPU.Backends
{
    /// <summary>
    /// Represents a generic high-level variable allocator.
    /// </summary>
    /// <remarks>The members of this class are not thread safe.</remarks>
    public abstract class VariableAllocator
    {
        #region Nested Types

        /// <summary>
        /// Represents an abstract variable.
        /// </summary>
        public abstract class Variable { }

        /// <summary>
        /// An intrinsic variable that can be accessed and allocated.
        /// </summary>
        public abstract class IntrinsicVariable : Variable
        {
            /// <summary>
            /// Constructs a new intrinsic variable.
            /// </summary>
            /// <param name="id">The current variable id.</param>
            internal IntrinsicVariable(int id)
            {
                Id = id;
            }

            /// <summary>
            /// Returns the unique variable id.
            /// </summary>
            public int Id { get; }

            /// <summary>
            /// Returns the string representation of this variable.
            /// </summary>
            /// <returns>The string representation of this variable.</returns>
            public override string ToString() =>
                "var" + Id;
        }

        /// <summary>
        /// A primitive variable.
        /// </summary>
        public sealed class PrimitiveVariable : IntrinsicVariable
        {
            /// <summary>
            /// Constructs a new primitive variable.
            /// </summary>
            /// <param name="id">The current variable id.</param>
            /// <param name="basicValueType">The basic value type.</param>
            internal PrimitiveVariable(int id, BasicValueType basicValueType)
                : base(id)
            {
                BasicValueType = basicValueType;
            }

            /// <summary>
            /// Returns the associated basic value type.
            /// </summary>
            public BasicValueType BasicValueType { get; }
        }

        /// <summary>
        /// A pointer variable.
        /// </summary>
        public sealed class PointerVariable : IntrinsicVariable
        {
            /// <summary>
            /// Constructs a new pointer variable.
            /// </summary>
            /// <param name="id">The current variable id.</param>
            /// <param name="elementType">The pointer element type.</param>
            /// <param name="addressSpace">The associated address space.</param>
            internal PointerVariable(
                int id,
                TypeNode elementType,
                MemoryAddressSpace addressSpace)
                : base(id)
            {
                AddressSpace = addressSpace;
                ElementType = elementType;
            }

            /// <summary>
            /// Returns the represented IR element type.
            /// </summary>
            public TypeNode ElementType { get; }

            /// <summary>
            /// Returns the associated address space.
            /// </summary>
            public MemoryAddressSpace AddressSpace { get; }
        }

        /// <summary>
        /// An object variable.
        /// </summary>
        public sealed class ObjectVariable : IntrinsicVariable
        {
            /// <summary>
            /// Constructs a new object variable.
            /// </summary>
            /// <param name="id">The current variable id.</param>
            /// <param name="type">The object type.</param>
            internal ObjectVariable(int id, ObjectType type)
                : base(id)
            {
                Type = type;
            }

            /// <summary>
            /// Returns the represented IR type.
            /// </summary>
            public ObjectType Type { get; }
        }

        /// <summary>
        /// A virtual view variable.
        /// </summary>
        public sealed class ViewVariable : Variable
        {
            /// <summary>
            /// Constructs a new view variable.
            /// </summary>
            /// <param name="viewType">The view type.</param>
            /// <param name="pointer">The pointer variable.</param>
            /// <param name="length">The length variable.</param>
            internal ViewVariable(
                ViewType viewType,
                PointerVariable pointer,
                PrimitiveVariable length)
            {
                Type = viewType;
                Pointer = pointer;
                Length = length;
            }

            /// <summary>
            /// Returns the represented IR type.
            /// </summary>
            public TypeNode Type { get; }

            /// <summary>
            /// Returns the associated pointer variable.
            /// </summary>
            public PointerVariable Pointer { get; }

            /// <summary>
            /// Returns the associated length variable.
            /// </summary>
            public PrimitiveVariable Length { get; }
        }

        #endregion

        #region Instance

        private readonly Dictionary<Value, Variable> variableLookup =
            new Dictionary<Value, Variable>();
        private int idCounter = 0;

        /// <summary>
        /// Constructs a new variable allocator.
        /// </summary>
        protected VariableAllocator() { }

        #endregion

        #region Methods

        /// <summary>
        /// Allocates a new variable.
        /// </summary>
        /// <param name="value">The value to allocate.</param>
        /// <returns>The allocated variable.</returns>
        public Variable Allocate(Value value)
        {
            if (variableLookup.TryGetValue(value, out Variable variable))
                return variable;
            variable = AllocateType(value.Type);
            variableLookup.Add(value, variable);
            return variable;
        }

        /// <summary>
        /// Allocates a new intrinsic variable.
        /// </summary>
        /// <param name="value">The value to allocate.</param>
        /// <returns>The allocated intrinsic variable.</returns>
        public IntrinsicVariable AllocateIntrinsic(Value value)
        {
            var variable = Allocate(value);
            if (variable is IntrinsicVariable intrinsicVariable)
                return intrinsicVariable;
            throw new NotSupportedException();
        }

        /// <summary>
        /// Allocates the given type.
        /// </summary>
        /// <param name="basicValueType">The type to allocate.</param>
        /// <returns>The allocated variable.</returns>
        public Variable AllocateType(BasicValueType basicValueType) =>
            new PrimitiveVariable(idCounter++, basicValueType);

        /// <summary>
        /// Allocates a pointer type.
        /// </summary>
        /// <param name="elementType">The pointer element type to allocate.</param>
        /// <param name="addressSpace">The associated address space.</param>
        /// <returns>The allocated variable.</returns>
        public PointerVariable AllocatePointerType(
            TypeNode elementType,
            MemoryAddressSpace addressSpace) =>
            new PointerVariable(idCounter++, elementType, addressSpace);

        /// <summary>
        /// Allocates the given type.
        /// </summary>
        /// <param name="typeNode">The type to allocate.</param>
        /// <returns>The allocated variable.</returns>
        public Variable AllocateType(TypeNode typeNode)
        {
            switch (typeNode)
            {
                case PrimitiveType primitiveType:
                    return AllocateType(primitiveType.BasicValueType);
                case PointerType pointerType:
                    return AllocatePointerType(pointerType.ElementType, pointerType.AddressSpace);
                case ViewType viewType:
                    return new ViewVariable(
                        viewType,
                        new PointerVariable(idCounter++, viewType.ElementType, viewType.AddressSpace),
                        new PrimitiveVariable(idCounter++, BasicValueType.Int32));
                case ObjectType objectType:
                    return new ObjectVariable(idCounter++, objectType);
                default:
                    throw new NotSupportedException();
            }
        }

        /// <summary>
        /// Loads the given value.
        /// </summary>
        /// <param name="value">The value to load.</param>
        /// <returns>The loaded variable.</returns>
        public Variable Load(Value value) =>
            variableLookup[value];

        /// <summary>
        /// Loads the given value as variable type <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">The target type to load.</typeparam>
        /// <param name="value">The value to load.</param>
        /// <returns>The loaded variable.</returns>
        public T LoadAs<T>(Value value)
            where T : Variable
        {
            var variable = Load(value);
            if (variable is T result)
                return result;
            throw new InvalidCodeGenerationException();
        }

        /// <summary>
        /// Loads the given value as variable type <see cref="IntrinsicVariable"/>.
        /// </summary>
        /// <param name="value">The value to load.</param>
        /// <returns>The loaded variable.</returns>
        public IntrinsicVariable LoadIntrinsic(Value value) =>
            LoadAs<IntrinsicVariable>(value);

        /// <summary>
        /// Binds the given value to the target variable.
        /// </summary>
        /// <param name="node">The node to bind.</param>
        /// <param name="targetVariable">The target variable to bind to.</param>
        public void Bind(Value node, Variable targetVariable) =>
            variableLookup[node] = targetVariable;

        #endregion
    }
}
