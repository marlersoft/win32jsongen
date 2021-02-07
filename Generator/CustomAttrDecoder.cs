// <copyright file="CustomAttrDecoder.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

#pragma warning disable SA1402 // File may only contain a single type

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
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

            if (code == PrimitiveTypeCode.Int16)
            {
                return CustomAttrType.Int16.Instance;
            }

            if (code == PrimitiveTypeCode.Int32)
            {
                return CustomAttrType.Int32.Instance;
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

        public CustomAttrType GetTypeFromSerializedName(string name)
        {
            if (name == "System.Runtime.InteropServices.UnmanagedType")
            {
                return CustomAttrType.UnmanagedType.Instance;
            }

            throw new NotImplementedException();
        }

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
        private static readonly Dictionary<Type, CustomAttrType> PrimitiveToCustomAttrTypeMap = new Dictionary<Type, CustomAttrType>();

        static CustomAttrType()
        {
            PrimitiveToCustomAttrTypeMap.Add(typeof(bool), CustomAttrType.Bool.Instance);
            PrimitiveToCustomAttrTypeMap.Add(typeof(short), CustomAttrType.Int16.Instance);
            PrimitiveToCustomAttrTypeMap.Add(typeof(int), CustomAttrType.Int32.Instance);
            PrimitiveToCustomAttrTypeMap.Add(typeof(System.Runtime.InteropServices.UnmanagedType), CustomAttrType.UnmanagedType.Instance);
        }

        internal static CustomAttrType ToCustomAttrType(Type type)
        {
            if (PrimitiveToCustomAttrTypeMap.TryGetValue(type, out CustomAttrType? result))
            {
                return result;
            }

            throw new ArgumentException(Fmt.In($"converting type '{type}' to a CustomAttrType is not implemented"));
        }

        internal abstract string FormatValue(object? value);

        internal class Bool : CustomAttrType
        {
            internal static readonly Bool Instance = new Bool();

            internal override string FormatValue(object? value) => Fmt.In($"Bool({value})");
        }

        internal class Int16 : CustomAttrType
        {
            internal static readonly Int16 Instance = new Int16();

            internal override string FormatValue(object? value) => Fmt.In($"Int16({value})");
        }

        internal class Int32 : CustomAttrType
        {
            internal static readonly Int32 Instance = new Int32();

            internal override string FormatValue(object? value) => Fmt.In($"Int32({value})");
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

    internal class CustomAttr
    {
        internal class Const : CustomAttr
        {
            internal static readonly Const Instance = new Const();

            private Const()
            {
            }
        }

        internal class NativeTypeInfo : CustomAttr
        {
            internal NativeTypeInfo(UnmanagedType unmanagedType, bool isNullTerminated, bool isNullNullTerminated, short? sizeParamIndex, UnmanagedType? arraySubType, int? sizeConst)
            {
                this.UnmanagedType = unmanagedType;
                this.IsNullTerminated = isNullTerminated;
                this.IsNullNullTerminated = isNullNullTerminated;
                this.SizeParamIndex = sizeParamIndex;
                this.ArraySubType = arraySubType;
                this.SizeConst = sizeConst;
            }

            internal UnmanagedType UnmanagedType { get; }

            internal bool IsNullTerminated { get; }

            internal bool IsNullNullTerminated { get; }

            internal short? SizeParamIndex { get; }

            internal UnmanagedType? ArraySubType { get; }

            internal int? SizeConst { get; }
        }

        internal class Obsolete : CustomAttr
        {
            internal Obsolete(string message)
            {
                this.Message = message;
            }

            internal string Message { get; }
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
    }
}