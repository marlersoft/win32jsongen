// <copyright file="CustomAttrDecoder.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

#pragma warning disable SA1402 // File may only contain a single type

namespace JsonWin32Generator
{
    using System;
    using System.Reflection.Metadata;
    using System.Runtime.InteropServices;

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
                return CustomAttrType.Bool.Instance;
            }

            if (code == PrimitiveTypeCode.String)
            {
                return CustomAttrType.Str.Instance;
            }

            throw new NotImplementedException("Only string and bool primitive types have been implemented for custom attributes");
        }

        public CustomAttrType GetSystemType() => CustomAttrType.SystemType.Instance;

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
                    return CustomAttrType.CallConv.Instance;
                }

                if (name == "UnmanagedType")
                {
                    return CustomAttrType.UnmanagedType.Instance;
                }
            }

            throw new NotImplementedException();
        }

        public CustomAttrType GetTypeFromSerializedName(string name) => throw new NotImplementedException();

        public PrimitiveTypeCode GetUnderlyingEnumType(CustomAttrType type)
        {
            if (object.ReferenceEquals(type, CustomAttrType.CallConv.Instance))
            {
                return PrimitiveTypeCode.Int32; // !!!!!!!! TODO: is this right???? What is this doing???
            }

            if (object.ReferenceEquals(type, CustomAttrType.UnmanagedType.Instance))
            {
                return PrimitiveTypeCode.Int32; // !!!!!!!! TODO: is this right???? What is this doing???
            }

            throw new NotImplementedException();
        }

        public bool IsSystemType(CustomAttrType type) => object.ReferenceEquals(type, CustomAttrType.SystemType.Instance);
    }

    internal abstract class CustomAttrType
    {
        internal abstract string FormatValue(object? value);

        internal class Bool : CustomAttrType
        {
            internal static readonly Bool Instance = new Bool();

            internal override string FormatValue(object? value) => Fmt.In($"Bool({value})");
        }

        internal class CallConv : CustomAttrType
        {
            internal static readonly CallConv Instance = new CallConv();

            internal override string FormatValue(object? value) => Fmt.In($"CallConv({(CallingConvention)value!})");
        }

        internal class SystemType : CustomAttrType
        {
            internal static readonly SystemType Instance = new SystemType();

            internal override string FormatValue(object? value) => Fmt.In($"Type({value})");
        }

        internal class Str : CustomAttrType
        {
            internal static readonly Str Instance = new Str();

            internal override string FormatValue(object? value) => Fmt.In($"String({value})");
        }

        internal class UnmanagedType : CustomAttrType
        {
            internal static readonly UnmanagedType Instance = new UnmanagedType();

            internal override string FormatValue(object? value) => Fmt.In($"UnmanagedType({value})");
        }
    }

    internal class ConstantAttr
    {
        internal static readonly ConstantAttr Instance = new ConstantAttr();

        internal class NativeTypeInfo : ConstantAttr
        {
            internal NativeTypeInfo(UnmanagedType unmanagedType, bool isNullTerminated)
            {
                this.UnmanagedType = unmanagedType;
                this.IsNullTerminated = isNullTerminated;
            }

            internal UnmanagedType UnmanagedType { get; }

            internal bool IsNullTerminated { get; }
        }

        internal class Obsolete : ConstantAttr
        {
            internal Obsolete(string message)
            {
                this.Message = message;
            }

            internal string Message { get; }
        }
    }

    internal class BasicTypeAttr
    {
        internal class Guid : BasicTypeAttr
        {
            internal Guid(string value)
            {
                this.Value = value;
            }

            internal string Value { get; }
        }

        internal class RaiiFree : BasicTypeAttr
        {
            internal RaiiFree(string freeFunc)
            {
                this.FreeFunc = freeFunc;
            }

            internal string FreeFunc { get; }
        }

        internal class NativeTypedef : BasicTypeAttr
        {
        }

        internal class UnmanagedFunctionPointer : BasicTypeAttr
        {
        }
    }
}
