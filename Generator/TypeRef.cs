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
                // TODO: can the array pointer be null?  for now I'm assuming all can.
                // TODO: take ArrayShape into account
                builder.Append("{\"Kind\":\"Array\",\"Child\":");
                this.ElementType.FormatTypeJson(builder);
                builder.Append('}');
            }
        }

        internal class RefOf : TypeRef
        {
            internal RefOf(TypeRef childType)
            {
                this.ChildType = childType;
            }

            internal TypeRef ChildType { get; }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                // TODO: include more information for ref type
                builder.Append("{\"Kind\":\"ReferenceTo\",\"Child\":");
                this.ChildType.FormatTypeJson(builder);
                builder.Append('}');
            }
        }

        internal class Ptr : TypeRef
        {
            internal Ptr(TypeRef childType)
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
            internal User(TypeGenInfo info)
            {
                this.Info = info;
            }

            internal TypeGenInfo Info { get; }

            internal override void FormatTypeJson(StringBuilder builder)
            {
                builder.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{{\"Kind\":\"ApiRef\",\"Name\":\"{0}\",\"Api\":\"{1}\",\"Parents\":[",
                    this.Info.Name,
                    this.Info.ApiName);
                TypeGenInfo? parentInfo = this.Info.EnclosingType;
                string prefix = string.Empty;
                while (parentInfo != null)
                {
                    builder.AppendFormat(CultureInfo.InvariantCulture, "{0}\"{1}\"", prefix, parentInfo.Name);
                    prefix = ",";
                    parentInfo = parentInfo.EnclosingType;
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
