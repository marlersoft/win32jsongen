// <copyright file="CustomAttrDecoder.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1201 // Elements should appear in the correct order

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Reflection.Metadata;

    internal class CustomAttrDecoder : ICustomAttributeTypeProvider<CustomAttrType>
    {
        internal static readonly CustomAttrDecoder Instance = new CustomAttrDecoder();

        private CustomAttrDecoder()
        {
        }

        public CustomAttrType GetPrimitiveType(PrimitiveTypeCode code)
        {
            if (code == PrimitiveTypeCode.Boolean)
            {
                return CustomAttrType.Bool;
            }

            if (code == PrimitiveTypeCode.Byte)
            {
                return CustomAttrType.Byte;
            }

            if (code == PrimitiveTypeCode.String)
            {
                return CustomAttrType.Str;
            }

            if (code == PrimitiveTypeCode.Int16)
            {
                return CustomAttrType.Int16;
            }

            if (code == PrimitiveTypeCode.UInt16)
            {
                return CustomAttrType.UInt16;
            }

            if (code == PrimitiveTypeCode.Int32)
            {
                return CustomAttrType.Int32;
            }

            if (code == PrimitiveTypeCode.UInt32)
            {
                return CustomAttrType.UInt32;
            }

            throw new NotImplementedException(Fmt.In($"convert PrimitiveTypeCode.{code} to CustomAttrType has not been implemented"));
        }

        public CustomAttrType GetSystemType() => CustomAttrType.SystemType;

        public CustomAttrType GetSZArrayType(CustomAttrType elementType) => throw new NotImplementedException();

        public CustomAttrType GetTypeFromDefinition(MetadataReader mr, TypeDefinitionHandle handle, byte rawTypeKind) => throw new NotImplementedException();

        public CustomAttrType GetTypeFromReference(MetadataReader mr, TypeReferenceHandle handle, byte rawTypeKind)
        {
            TypeReference typeRef = mr.GetTypeReference(handle);
            string @namespace = mr.GetString(typeRef.Namespace);
            string name = mr.GetString(typeRef.Name);
            if (@namespace == "System.Runtime.InteropServices")
            {
                if (name == "CallingConvention")
                {
                    return CustomAttrType.CallConv;
                }

                if (name == "UnmanagedType")
                {
                    return CustomAttrType.UnmanagedType;
                }
            }

            if (@namespace == "Windows.Win32.Interop")
            {
                if (name == "Architecture")
                {
                    return CustomAttrType.Architecture;
                }
            }

            throw new NotImplementedException();
        }

        public CustomAttrType GetTypeFromSerializedName(string name)
        {
            if (name == "System.Runtime.InteropServices.UnmanagedType")
            {
                return CustomAttrType.UnmanagedType;
            }

            throw new NotImplementedException();
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(CustomAttrType type)
        {
            if (type == CustomAttrType.CallConv)
            {
                return PrimitiveTypeCode.Int32; // !!!!!!!! TODO: is this right???? What is this doing???
            }

            if (type == CustomAttrType.UnmanagedType)
            {
                return PrimitiveTypeCode.Int32; // !!!!!!!! TODO: is this right???? What is this doing???
            }

            if (type == CustomAttrType.Architecture)
            {
                return PrimitiveTypeCode.Int32;
            }

            throw new NotImplementedException();
        }

        public bool IsSystemType(CustomAttrType type) => type == CustomAttrType.SystemType;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:Elements should appear in the correct order", Justification = "Meh")]
    internal enum CustomAttrType
    {
        Bool,
        Byte,
        Int16,
        UInt16,
        Int32,
        UInt32,
        UnmanagedType,
        CallConv,
        SystemType,
        Str,
        Architecture,
    }

    internal static class CustomAttrTypeMap
    {
        private static readonly Dictionary<Type, CustomAttrType> ClrTypeToCustomAttrTypeMap = new Dictionary<Type, CustomAttrType>();

        static CustomAttrTypeMap()
        {
            ClrTypeToCustomAttrTypeMap.Add(typeof(bool), CustomAttrType.Bool);
            ClrTypeToCustomAttrTypeMap.Add(typeof(byte), CustomAttrType.Byte);
            ClrTypeToCustomAttrTypeMap.Add(typeof(short), CustomAttrType.Int16);
            ClrTypeToCustomAttrTypeMap.Add(typeof(ushort), CustomAttrType.UInt16);
            ClrTypeToCustomAttrTypeMap.Add(typeof(int), CustomAttrType.Int32);
            ClrTypeToCustomAttrTypeMap.Add(typeof(uint), CustomAttrType.UInt32);
            ClrTypeToCustomAttrTypeMap.Add(typeof(string), CustomAttrType.Str);
            ClrTypeToCustomAttrTypeMap.Add(typeof(System.Runtime.InteropServices.UnmanagedType), CustomAttrType.UnmanagedType);
            ClrTypeToCustomAttrTypeMap.Add(typeof(Architecture), CustomAttrType.Architecture);
        }

        internal static CustomAttrType FromType(this Type type)
        {
            if (ClrTypeToCustomAttrTypeMap.TryGetValue(type, out CustomAttrType result))
            {
                return result;
            }

            throw new ArgumentException(Fmt.In($"converting type '{type}' to a CustomAttrType is not implemented"));
        }
    }

    // This type should be identical to the type defined in win32metadata
    [Flags]
    internal enum Architecture
    {
        None = 0,
        X86 = 1,
        X64 = 2,
        Arm64 = 4,
        All = Architecture.X64 | Architecture.X86 | Architecture.Arm64,
    }

    internal enum Arch
    {
        X86,
        X64,
        Arm64,
    }

    internal class CustomAttr
    {
        private static readonly Dictionary<Architecture, Arch[]> ArchArrayCache = new Dictionary<Architecture, Arch[]>();

        internal static Arch[] GetArchLimit(Architecture archFlags)
        {
            if (ArchArrayCache.TryGetValue(archFlags, out Arch[]? existing))
            {
                return existing;
            }

            Architecture remainingFlags = archFlags;
            List<Arch> archList = new List<Arch>();
            if (ConsumeFlag(ref remainingFlags, Architecture.X86))
            {
                archList.Add(Arch.X86);
            }

            if (ConsumeFlag(ref remainingFlags, Architecture.X64))
            {
                archList.Add(Arch.X64);
            }

            if (ConsumeFlag(ref remainingFlags, Architecture.Arm64))
            {
                archList.Add(Arch.Arm64);
            }

            Enforce.Data(remainingFlags == 0);

            Arch[] archArray = archList.ToArray();
            ArchArrayCache.Add(archFlags, archArray);
            return archArray;
        }

        internal static CustomAttr Decode(MetadataReader mr, CustomAttributeHandle attrHandle)
        {
            CustomAttribute attr = mr.GetCustomAttribute(attrHandle);
            NamespaceAndName attrName = attr.GetAttrTypeName(mr);
            CustomAttributeValue<CustomAttrType> attrArgs = attr.DecodeValue(CustomAttrDecoder.Instance);
            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "ConstAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.Const.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "NativeArrayInfoAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                short? countParamIndex = null;
                int? countConst = null;
                foreach (CustomAttributeNamedArgument<CustomAttrType> namedArg in attrArgs.NamedArguments)
                {
                    if (namedArg.Name == "CountConst")
                    {
                        Enforce.Data(!countConst.HasValue);
                        countConst = Enforce.NamedAttrAs<int>(namedArg);
                    }
                    else if (namedArg.Name == "CountParamIndex")
                    {
                        Enforce.Data(!countParamIndex.HasValue);
                        countParamIndex = Enforce.NamedAttrAs<short>(namedArg);
                    }
                    else
                    {
                        Violation.Data();
                    }
                }

                return new NativeArrayInfo(countConst, countParamIndex);
            }

            if (attrName == new NamespaceAndName("System", "ObsoleteAttribute"))
            {
                string message = string.Empty;
                if (attrArgs.FixedArguments.Length == 1)
                {
                    message = Enforce.FixedAttrAs<string>(attrArgs.FixedArguments[0]);
                }
                else
                {
                    Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                }

                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.Obsolete(message);
            }

            if (attrName == new NamespaceAndName("System", "FlagsAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.Flags.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "GuidAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 11);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.Guid(DecodeGuid(attrArgs.FixedArguments, 0));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "PropertyKeyAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 12);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.ProperyKey(
                    DecodeGuid(attrArgs.FixedArguments, 0),
                    Enforce.FixedAttrAs<uint>(attrArgs.FixedArguments[11]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "RAIIFreeAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.RaiiFree(Enforce.FixedAttrAs<string>(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "NativeTypedefAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.NativeTypedef.Instance;
            }

            if (attrName == new NamespaceAndName("System.Runtime.InteropServices", "UnmanagedFunctionPointerAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.UnmanagedFunctionPointer.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "ComOutPtrAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.ComOutPtr.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "NotNullTerminatedAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.NotNullTerminated.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "NullNullTerminatedAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.NullNullTerminated.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "RetValAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.RetVal.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "FreeWithAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.FreeWith(Enforce.FixedAttrAs<string>(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "SupportedOSPlatformAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.SupportedOSPlatform(Enforce.FixedAttrAs<string>(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "AlsoUsableForAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.AlsoUsableFor(Enforce.FixedAttrAs<string>(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "MemorySizeAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 1);
                Enforce.Data(attrArgs.NamedArguments[0].Name == "BytesParamIndex");
                return new CustomAttr.MemorySize(Enforce.NamedAttrAs<short>(attrArgs.NamedArguments[0]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "SupportedArchitectureAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.SupportedArchitecture(Enforce.FixedAttrAs<Architecture>(attrArgs.FixedArguments[0]));
            }

            throw new NotImplementedException(Fmt.In($"unhandled custom attribute \"{attrName.Namespace}\", \"{attrName.Name}\""));
        }

        internal static string DecodeGuid(ImmutableArray<CustomAttributeTypedArgument<CustomAttrType>> fixedArgs, int offset)
        {
            return new System.Guid(
                Enforce.FixedAttrAs<uint>(fixedArgs[offset + 0]),
                Enforce.FixedAttrAs<ushort>(fixedArgs[offset + 1]),
                Enforce.FixedAttrAs<ushort>(fixedArgs[offset + 2]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 3]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 4]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 5]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 6]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 7]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 8]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 9]),
                Enforce.FixedAttrAs<byte>(fixedArgs[offset + 10])).ToString();
        }

        private static bool ConsumeFlag(ref Architecture archFlags, Architecture flag)
        {
            if ((archFlags & flag) == flag)
            {
                archFlags &= ~flag;
                return true;
            }

            return false;
        }

        internal class Const : CustomAttr
        {
            internal static readonly Const Instance = new Const();

            private Const()
            {
            }
        }

        internal class NativeArrayInfo : CustomAttr
        {
            internal NativeArrayInfo(int? countConst, short? countParamIndex)
            {
                this.CountConst = countConst;
                this.CountParamIndex = countParamIndex;
            }

            internal int? CountConst { get; }

            internal short? CountParamIndex { get; }
        }

        internal class Obsolete : CustomAttr
        {
            internal Obsolete(string message)
            {
                this.Message = message;
            }

            internal string Message { get; }
        }

        internal class Flags : CustomAttr
        {
            internal static readonly Flags Instance = new Flags();

            internal Flags()
            {
            }
        }

        internal class Guid : CustomAttr
        {
            internal Guid(string value)
            {
                this.Value = value;
            }

            internal string Value { get; }
        }

        internal class ProperyKey : CustomAttr
        {
            internal ProperyKey(string fmtid, uint pid)
            {
                this.Fmtid = fmtid;
                this.Pid = pid;
            }

            internal string Fmtid { get; }

            internal uint Pid { get; }
        }

        internal class RaiiFree : CustomAttr
        {
            internal RaiiFree(string freeFunc)
            {
                this.FreeFunc = freeFunc;
            }

            internal string FreeFunc { get; }
        }

        internal class NativeTypedef : CustomAttr
        {
            internal static readonly NativeTypedef Instance = new NativeTypedef();

            private NativeTypedef()
            {
            }
        }

        internal class UnmanagedFunctionPointer : CustomAttr
        {
            internal static readonly UnmanagedFunctionPointer Instance = new UnmanagedFunctionPointer();

            private UnmanagedFunctionPointer()
            {
            }
        }

        internal class ComOutPtr : CustomAttr
        {
            internal static readonly ComOutPtr Instance = new ComOutPtr();

            private ComOutPtr()
            {
            }
        }

        internal class NotNullTerminated : CustomAttr
        {
            internal static readonly NotNullTerminated Instance = new NotNullTerminated();

            private NotNullTerminated()
            {
            }
        }

        internal class NullNullTerminated : CustomAttr
        {
            internal static readonly NullNullTerminated Instance = new NullNullTerminated();

            private NullNullTerminated()
            {
            }
        }

        internal class RetVal : CustomAttr
        {
            internal static readonly RetVal Instance = new RetVal();

            private RetVal()
            {
            }
        }

        internal class FreeWith : CustomAttr
        {
            internal FreeWith(string name)
            {
                this.Name = name;
            }

            internal string Name { get; }
        }

        internal class SupportedOSPlatform : CustomAttr
        {
            internal SupportedOSPlatform(string platformName)
            {
                this.PlatformName = platformName;
            }

            internal string PlatformName { get; }
        }

        internal class AlsoUsableFor : CustomAttr
        {
            internal AlsoUsableFor(string otherType)
            {
                this.OtherType = otherType;
            }

            internal string OtherType { get; }
        }

        internal class MemorySize : CustomAttr
        {
            internal MemorySize(short bytesParamIndex)
            {
                this.BytesParamIndex = bytesParamIndex;
            }

            internal short BytesParamIndex { get; }
        }

        internal class SupportedArchitecture : CustomAttr
        {
            internal SupportedArchitecture(Architecture archFlags)
            {
                this.ArchFlags = archFlags;
            }

            internal Architecture ArchFlags { get; }
        }
    }
}