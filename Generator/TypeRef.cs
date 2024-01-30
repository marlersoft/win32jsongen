// <copyright file="TypeRef.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System;
    using System.Globalization;
    using System.Reflection.Metadata;
    using System.Text;

    internal abstract class TypeRef
    {
        internal string ToJson()
        {
            StringBuilder builder = new StringBuilder();
            this.FormatTypeJson(builder);
            return builder.ToString();
        }

        internal abstract void FormatTypeJson(StringBuilder builder);

        private TypeRef GetChildType(TypeRefDecoder decoder)
        {
            if (this is TypeRef.PointerTo pointerTo)
            {
                return pointerTo.ChildType;
            }

            if (object.ReferenceEquals(this, Primitive.IntPtr))
            {
                return Primitive.Void;
            }

            if (this is TypeRef.User userType)
            {
                if (userType.Info.Fqn == "Windows.Win32.Foundation.PSTR")
                {
                    return TypeRef.Primitive.Byte;
                }

                if (userType.Info.Fqn == "Windows.Win32.Foundation.PWSTR")
                {
                    return TypeRef.Primitive.Char;
                }

                // This is the type of a parameter for PxeProviderSetAttribute that changes dynamically depending on another
                // parameter, so we'll just default to representing it as a Byte array.
                if (userType.Info.Fqn == "Windows.Win32.WindowsDeploymentServices.PxeProviderSetAttribute_pParameterBufferFlags")
                {
                    return TypeRef.Primitive.Byte;
                }

                // I think this is an array of bytes based on https://docs.microsoft.com/en-us/windows/win32/api/wingdi/nf-wingdi-getpath
                // aj: A pointer to an array of bytes that receives the vertex types.
                if (userType.Info.Fqn == "Windows.Win32.Gdi.GetPath_aj")
                {
                    return TypeRef.Primitive.Byte;
                }

                if (userType.Info.Fqn == "Windows.Win32.Security.PSID")
                {
                    return decoder.GetTypeFromNamespaceAndNameInThisModule("Windows.Win32.Security", "SID");
                }
            }

            throw Violation.Data();
        }

        internal class ArrayOf : TypeRef
        {
            internal ArrayOf(TypeRef elementType, ArrayShape shape)
            {
                this.ElementType = elementType;
                this.Shape = shape;
            }

            internal TypeRef ElementType { get; }

            internal ArrayShape Shape { get; }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                string shape = "null";
                Enforce.Data(this.Shape.Rank == 1);
                Enforce.Data(this.Shape.LowerBounds.Length == 1);
                Enforce.Data(this.Shape.Sizes.Length == 1);
                int size = this.Shape.Sizes[0];
                if (size != 1)
                {
                    shape = Fmt.In($"{{\"Size\":{size}}}");
                }

                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{{\"Kind\":\"Array\",\"Shape\":{0},\"Child\":",
                    shape);
                this.ElementType.FormatTypeJson(builder);
                builder.Append('}');
            }
        }

        internal class PointerTo : TypeRef
        {
            internal PointerTo(TypeRef childType)
            {
                this.ChildType = childType;
            }

            internal TypeRef ChildType { get; }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                // TODO: include more information for pointer type
                builder.Append("{\"Kind\":\"PointerTo\",\"Child\":");
                this.ChildType.FormatTypeJson(builder);
                builder.Append('}');
            }
        }

        internal class User : TypeRef
        {
            internal User(TypeRefInfo info)
            {
                this.Info = info;
            }

            internal TypeRefInfo Info { get; }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{{\"Kind\":\"ApiRef\",\"Name\":\"{0}\",\"TargetKind\":\"{1}\",\"Api\":\"{2}\",\"IsNativeTypedef\":{3},\"Parents\":[",
                    this.Info.Name,
                    this.Info.TypeRefTargetKind,
                    this.Info.ApiName,
                    this.Info.IsNativeTypedef.Json());
                string prefix = string.Empty;
                foreach (string parentTypeQualifier in this.Info.ParentTypeQualifier.Qualifiers)
                {
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0}\"{1}\"", prefix, parentTypeQualifier);
                    prefix = ",";
                }

                builder.Append("]}");
            }
        }

        internal class Primitive : TypeRef
        {
            public static readonly Primitive Void = new Primitive(PrimitiveTypeCode.Void);
            public static readonly Primitive Boolean = new Primitive(PrimitiveTypeCode.Boolean);
            public static readonly Primitive Char = new Primitive(PrimitiveTypeCode.Char);
            public static readonly Primitive SByte = new Primitive(PrimitiveTypeCode.SByte);
            public static readonly Primitive Byte = new Primitive(PrimitiveTypeCode.Byte);
            public static readonly Primitive Int16 = new Primitive(PrimitiveTypeCode.Int16);
            public static readonly Primitive UInt16 = new Primitive(PrimitiveTypeCode.UInt16);
            public static readonly Primitive Int32 = new Primitive(PrimitiveTypeCode.Int32);
            public static readonly Primitive UInt32 = new Primitive(PrimitiveTypeCode.UInt32);
            public static readonly Primitive Int64 = new Primitive(PrimitiveTypeCode.Int64);
            public static readonly Primitive UInt64 = new Primitive(PrimitiveTypeCode.UInt64);
            public static readonly Primitive Single = new Primitive(PrimitiveTypeCode.Single);
            public static readonly Primitive Double = new Primitive(PrimitiveTypeCode.Double);
            public static readonly Primitive String = new Primitive(PrimitiveTypeCode.String);
            public static readonly Primitive TypedReference = new Primitive(PrimitiveTypeCode.TypedReference);
            public static readonly Primitive IntPtr = new Primitive(PrimitiveTypeCode.IntPtr);
            public static readonly Primitive UIntPtr = new Primitive(PrimitiveTypeCode.UIntPtr);
            public static readonly Primitive Object = new Primitive(PrimitiveTypeCode.Object);

            private Primitive(PrimitiveTypeCode code) => this.Code = code;

            internal PrimitiveTypeCode Code { get; }

            internal static Primitive Get(PrimitiveTypeCode code) => code switch
            {
                PrimitiveTypeCode.Void => Void,
                PrimitiveTypeCode.Boolean => Boolean,
                PrimitiveTypeCode.Char => Char,
                PrimitiveTypeCode.SByte => SByte,
                PrimitiveTypeCode.Byte => Byte,
                PrimitiveTypeCode.Int16 => Int16,
                PrimitiveTypeCode.UInt16 => UInt16,
                PrimitiveTypeCode.Int32 => Int32,
                PrimitiveTypeCode.UInt32 => UInt32,
                PrimitiveTypeCode.Int64 => Int64,
                PrimitiveTypeCode.UInt64 => UInt64,
                PrimitiveTypeCode.Single => Single,
                PrimitiveTypeCode.Double => Double,
                PrimitiveTypeCode.String => String,
                PrimitiveTypeCode.TypedReference => TypedReference,
                PrimitiveTypeCode.IntPtr => IntPtr,
                PrimitiveTypeCode.UIntPtr => UIntPtr,
                PrimitiveTypeCode.Object => Object,
                _ => throw new InvalidOperationException(),
            };

            internal override void FormatTypeJson(StringBuilder builder)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "{{\"Kind\":\"Native\",\"Name\":\"{0}\"}}", this.Code);
            }
        }

        internal class Guid : TypeRef
        {
            internal static readonly Guid Instance = new Guid();

            private Guid()
            {
            }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                builder.Append("{\"Kind\":\"Native\",\"Name\":\"Guid\"}");
            }
        }

        internal class LPArray : TypeRef
        {
            internal LPArray(CustomAttr.NativeArrayInfo info, TypeRef typeRef, TypeRefDecoder typeRefDecoder)
            {
                this.NullNullTerm = false;
                this.CountConst = info.CountConst ?? -1;
                this.CountParamIndex = info.CountParamIndex ?? -1;
                this.ChildType = typeRef.GetChildType(typeRefDecoder);
            }

            internal bool NullNullTerm { get; }

            internal int CountConst { get; }

            internal short CountParamIndex { get; }

            internal TypeRef ChildType { get; }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                builder.Append(Fmt.In($"{{\"Kind\":\"LPArray\",\"NullNullTerm\":{this.NullNullTerm.Json()},\"CountConst\":{this.CountConst},\"CountParamIndex\":{this.CountParamIndex},\"Child\":"));
                this.ChildType.FormatTypeJson(builder);
                builder.Append('}');
            }
        }

        internal class MissingClrType : TypeRef
        {
            private readonly string @namespace;
            private readonly string name;

            internal MissingClrType(string @namespace, string name)
            {
                this.@namespace = @namespace;
                this.name = name;
            }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                builder.AppendFormat(CultureInfo.InvariantCulture, "{{\"Kind\":\"MissingClrType\",\"Name\":\"{0}\",\"Namespace\":\"{1}\"}}", this.name, this.@namespace);
            }
        }
    }
}
