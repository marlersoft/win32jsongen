// <copyright file="CustomAttrDecoder.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

#pragma warning disable SA1402 // File may only contain a single type

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
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

            if (code == PrimitiveTypeCode.String)
            {
                return CustomAttrType.Str;
            }

            if (code == PrimitiveTypeCode.Int16)
            {
                return CustomAttrType.Int16;
            }

            if (code == PrimitiveTypeCode.Int32)
            {
                return CustomAttrType.Int32;
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

            throw new NotImplementedException();
        }

        public bool IsSystemType(CustomAttrType type) => type == CustomAttrType.SystemType;
    }

    internal enum CustomAttrType
    {
        Bool,
        Int16,
        Int32,
        UnmanagedType,
        CallConv,
        SystemType,
        Str,
    }

    internal static class CustomAttrTypeMap
    {
        private static readonly Dictionary<Type, CustomAttrType> ClrTypeToCustomAttrTypeMap = new Dictionary<Type, CustomAttrType>();

        static CustomAttrTypeMap()
        {
            ClrTypeToCustomAttrTypeMap.Add(typeof(bool), CustomAttrType.Bool);
            ClrTypeToCustomAttrTypeMap.Add(typeof(short), CustomAttrType.Int16);
            ClrTypeToCustomAttrTypeMap.Add(typeof(int), CustomAttrType.Int32);
            ClrTypeToCustomAttrTypeMap.Add(typeof(string), CustomAttrType.Str);
            ClrTypeToCustomAttrTypeMap.Add(typeof(System.Runtime.InteropServices.UnmanagedType), CustomAttrType.UnmanagedType);
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

    internal class CustomAttr
    {
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
                short? sizeParamIndex = null;
                int? sizeConst = null;
                short? bytesParamIndex = null;
                foreach (CustomAttributeNamedArgument<CustomAttrType> namedArg in attrArgs.NamedArguments)
                {
                    if (namedArg.Name == "SizeParamIndex")
                    {
                        Enforce.Data(!sizeParamIndex.HasValue);
                        sizeParamIndex = Enforce.NamedAttrAs<short>(namedArg);
                    }
                    else if (namedArg.Name == "SizeConst")
                    {
                        Enforce.Data(!sizeConst.HasValue);
                        sizeConst = Enforce.NamedAttrAs<int>(namedArg);
                    }
                    else if (namedArg.Name == "BytesParamIndex")
                    {
                        Enforce.Data(!bytesParamIndex.HasValue);
                        bytesParamIndex = Enforce.NamedAttrAs<short>(namedArg);
                    }
                    else
                    {
                        Violation.Data();
                    }
                }

                return new NativeArrayInfo(sizeParamIndex, sizeConst, bytesParamIndex);
            }

            if (attrName == new NamespaceAndName("System", "ObsoleteAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.Obsolete(Enforce.FixedAttrAs<string>(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("System", "FlagsAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.Flags.Instance;
            }

            if (attrName == new NamespaceAndName("System.Runtime.InteropServices", "GuidAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.Guid(Enforce.FixedAttrAs<string>(attrArgs.FixedArguments[0]));
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

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "GuidConstAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.GuidConst.Instance;
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

            throw new NotImplementedException(Fmt.In($"unhandled custom attribute \"{attrName.Namespace}\", \"{attrName.Name}\""));
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
            internal NativeArrayInfo(short? sizeParamIndex, int? sizeConst, short? bytesParamIndex)
            {
                this.SizeParamIndex = sizeParamIndex;
                this.SizeConst = sizeConst;
                this.BytesParamIndex = bytesParamIndex;
            }

            internal short? SizeParamIndex { get; }

            internal int? SizeConst { get; }

            internal short? BytesParamIndex { get; }
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

        internal class GuidConst : CustomAttr
        {
            internal static readonly GuidConst Instance = new GuidConst();

            private GuidConst()
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
    }
}