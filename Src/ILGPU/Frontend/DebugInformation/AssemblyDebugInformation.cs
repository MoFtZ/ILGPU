// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: AssemblyDebugInformation.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;

namespace ILGPU.Frontend.DebugInformation
{
    /// <summary>
    /// Represents assembly debug information.
    /// </summary>
    public sealed class AssemblyDebugInformation : IMetadataReaderOperationProvider
    {
        #region Static

        /// <summary>
        /// A minimal PDB file without any metadata, following ECMA-335 spec.
        /// </summary>
        private static readonly ImmutableArray<byte> EmptyPdb =
            ImmutableArray.Create<byte>(
            [
                // Metadata root – BSJB signature
                0x42, 0x53, 0x4A, 0x42,
                // MajorVersion, MinorVersion
                0x01, 0x00, 0x01, 0x00,
                // Reserved
                0x00, 0x00, 0x00, 0x00,
                // VersionLength = 12 (padded)
                0x0C, 0x00, 0x00, 0x00,
                // Version string "v4.0.30319\0" + 1 pad byte
                0x76, 0x34, 0x2E, 0x30, 0x2E, 0x33, 0x30, 0x33, 0x31, 0x39, 0x00, 0x00,
                // Flags, NumberOfStreams = 5
                0x00, 0x00, 0x05, 0x00,

                // Stream header: #~  (offset=0x6C, size=0x26)
                0x6C, 0x00, 0x00, 0x00,
                0x26, 0x00, 0x00, 0x00,
                0x23, 0x7E, 0x00, 0x00, // "#~\0\0"

                // Stream header: #Strings  (offset=0x92, size=0x01)
                0x92, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x23, 0x53, 0x74, 0x72, 0x69, 0x6E, 0x67, 0x73, // "#Strings"
                0x00, 0x00, 0x00, 0x00,                         // \0 + 3 pad

                // Stream header: #US  (offset=0x93, size=0x01)
                0x93, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x23, 0x55, 0x53, 0x00, // "#US\0"

                // Stream header: #Blob  (offset=0x94, size=0x01)
                0x94, 0x00, 0x00, 0x00,
                0x01, 0x00, 0x00, 0x00,
                0x23, 0x42, 0x6C, 0x6F, 0x62, 0x00, 0x00, 0x00, // "#Blob\0\0\0"

                // Stream header: #GUID  (offset=0x95, size=0x10)
                0x95, 0x00, 0x00, 0x00,
                0x10, 0x00, 0x00, 0x00,
                0x23, 0x47, 0x55, 0x49, 0x44, 0x00, 0x00, 0x00, // "#GUID\0\0\0"

                // #~ stream
                0x00, 0x00, 0x00, 0x00, // Reserved
                0x02, 0x00, 0x00, 0x01, // MajorVersion, MinorVersion, HeapSizes, Reserved
                // Valid: only Module table (bit 0 set)
                0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                // Sorted (standard mask)
                0x01, 0x33, 0x00, 0x16, 0x00, 0x00, 0x00, 0x00,
                // RowCount[0] = 1  (Module table has 1 row)
                0x01, 0x00, 0x00, 0x00,
                // Module table row
                0x00, 0x00, // Generation = 0
                0x00, 0x00, // Name       -> #Strings[0] = ""
                0x01, 0x00, // Mvid       -> #GUID[1]
                0x00, 0x00, // EncId      = 0
                0x00, 0x00, // EncBaseId  = 0

                // #Strings heap
                0x00,

                // #US heap
                0x00,

                // #Blob heap
                0x00,

                // #GUID heap – module version ID (MVID)
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            ]);

        #endregion

        #region Instance

        /// <summary>
        /// The internal mapping of methods to cached debug information.
        /// </summary>
        private readonly Dictionary<MethodBase, MethodDebugInformation>
            debugInformation =
            new Dictionary<MethodBase, MethodDebugInformation>();

        /// <summary>
        /// The internal reader provider.
        /// </summary>
        private readonly MetadataReaderProvider readerProvider;

        /// <summary>
        /// The internal synchronization object.
        /// </summary>
        private readonly object syncLock = new object();

        /// <summary>
        /// Constructs new empty assembly debug information.
        /// </summary>
        /// <param name="assembly">The referenced assembly.</param>
        internal AssemblyDebugInformation(Assembly assembly)
        {
            Assembly = assembly;
            Modules = ImmutableArray<Module>.Empty;

            readerProvider = MetadataReaderProvider.FromPortablePdbImage(EmptyPdb);
            MetadataReader = readerProvider.GetMetadataReader();
        }

        /// <summary>
        /// Constructs new assembly debug information.
        /// </summary>
        /// <param name="assembly">The referenced assembly.</param>
        /// <param name="pdbStream">
        /// The associated PDB stream (hast to be kept open).
        /// </param>
        internal AssemblyDebugInformation(Assembly assembly, Stream pdbStream)
        {
            Assembly = assembly;
            Modules = ImmutableArray.Create(assembly.GetModules());

            readerProvider = MetadataReaderProvider.FromPortablePdbStream(
                pdbStream,
                MetadataStreamOptions.Default);
            MetadataReader = readerProvider.GetMetadataReader();

            foreach (var methodHandle in MetadataReader.MethodDebugInformation)
            {
                var definitionHandle = methodHandle.ToDefinitionHandle();
                var metadataToken = MetadataTokens.GetToken(definitionHandle);
                if (TryResolveMethod(metadataToken, out MethodBase? method))
                {
                    debugInformation.Add(
                        method,
                        new MethodDebugInformation(
                            this,
                            method,
                            definitionHandle));
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated assembly.
        /// </summary>
        public Assembly Assembly { get; }

        /// <summary>
        /// Returns the associated modules.
        /// </summary>
        public ImmutableArray<Module> Modules { get; }

        /// <summary>
        /// Returns true if this container holds valid debug information.
        /// </summary>
        public bool IsValid => !Modules.IsDefaultOrEmpty;

        /// <summary>
        /// Returns the associated metadata reader.
        /// </summary>
        private MetadataReader MetadataReader { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Begins a synchronized metadata reader operation.
        /// </summary>
        /// <returns>The operation instance.</returns>
        MetadataReaderOperation IMetadataReaderOperationProvider.BeginOperation() =>
            new MetadataReaderOperation(MetadataReader, syncLock);

        /// <summary>
        /// Tries to resolve the given metadata token to a method.
        /// </summary>
        /// <param name="metadataToken">The metadata token to resolve.</param>
        /// <param name="method">The resolved method (or null).</param>
        /// <returns>True, if the given token could be resolved.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolveMethod(
            int metadataToken,
            [NotNullWhen(true)] out MethodBase? method)
        {
            foreach (var module in Modules)
            {
                method = module.ResolveMethod(metadataToken);
                if (method != null)
                    return true;
            }
            method = null;
            return false;
        }

        /// <summary>
        /// Tries to load debug information for the given method base.
        /// </summary>
        /// <param name="methodBase">The method base.</param>
        /// <param name="methodDebugInformation">
        /// The loaded debug information (or null).
        /// </param>
        /// <returns>True, if the requested debug information could be loaded.</returns>
        public bool TryLoadDebugInformation(
            MethodBase methodBase,
            [NotNullWhen(true)] out MethodDebugInformation? methodDebugInformation)
        {
            Debug.Assert(methodBase != null, "Invalid method");
            Debug.Assert(
                methodBase.Module.Assembly == Assembly,
                "Invalid method association");

            if (methodBase is MethodInfo methodInfo &&
                methodInfo.GetGenericArguments().Length > 0)
            {
                methodBase = methodInfo.GetGenericMethodDefinition();
            }
            return debugInformation.TryGetValue(
                methodBase,
                out methodDebugInformation);
        }

        #endregion
    }
}
