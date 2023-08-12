// <copyright file="Util.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

#pragma warning disable SA1649 // File name should match first type name
#pragma warning disable SA1402 // File may only contain a single type

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Text;

    internal record NamespaceAndName(string Namespace, string Name);

    internal interface INothing
    {
    }

    internal readonly struct Defer : IDisposable
    {
        private readonly Action action;

        private Defer(Action action) => this.action = action;

        void IDisposable.Dispose() => this.action();

        internal static Defer Do(Action action) => new Defer(action);
    }

    internal static class Metadata
    {
        internal const string WindowsWin32NamespacePrefix = "Windows.Win32.";
    }

    // Shorthand for Formattable.Invariant
    internal static class Fmt
    {
        internal static string In(FormattableString s) => FormattableString.Invariant(s);
    }

    internal static class Extensions
    {
        internal static TValue GetOrCreate<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key)
            where TValue : new()
        {
            if (!dict.TryGetValue(key, out TValue val))
            {
                val = new TValue();
                dict.Add(key, val);
            }

            return val;
        }

        internal static PrimitiveTypeCode ToPrimitiveTypeCode(this ConstantTypeCode code) => code switch
        {
            ConstantTypeCode.Boolean => PrimitiveTypeCode.Boolean,
            ConstantTypeCode.Char => PrimitiveTypeCode.Char,
            ConstantTypeCode.SByte => PrimitiveTypeCode.SByte,
            ConstantTypeCode.Byte => PrimitiveTypeCode.Byte,
            ConstantTypeCode.Int16 => PrimitiveTypeCode.Int16,
            ConstantTypeCode.UInt16 => PrimitiveTypeCode.UInt16,
            ConstantTypeCode.Int32 => PrimitiveTypeCode.Int32,
            ConstantTypeCode.UInt32 => PrimitiveTypeCode.UInt32,
            ConstantTypeCode.Int64 => PrimitiveTypeCode.Int64,
            ConstantTypeCode.UInt64 => PrimitiveTypeCode.UInt64,
            ConstantTypeCode.Single => PrimitiveTypeCode.Single,
            ConstantTypeCode.Double => PrimitiveTypeCode.Double,
            ConstantTypeCode.String => PrimitiveTypeCode.String,
            ConstantTypeCode.NullReference => throw new NotSupportedException("a NullReference const"),
            _ => throw new InvalidOperationException(),
        };

        internal static NamespaceAndName GetAttrTypeName(this CustomAttribute attr, MetadataReader mr)
        {
            if (attr.Constructor.Kind == HandleKind.MemberReference)
            {
                MemberReference member_ref = mr.GetMemberReference((MemberReferenceHandle)attr.Constructor);
                TypeReference parent_ref = mr.GetTypeReference((TypeReferenceHandle)member_ref.Parent);
                return new NamespaceAndName(mr.GetString(parent_ref.Namespace), mr.GetString(parent_ref.Name));
            }

            if (attr.Constructor.Kind == HandleKind.MethodDefinition)
            {
                MethodDefinition method_def = mr.GetMethodDefinition((MethodDefinitionHandle)attr.Constructor);
                TypeDefinition type_def = mr.GetTypeDefinition(method_def.GetDeclaringType());
                return new NamespaceAndName(mr.GetString(type_def.Namespace), mr.GetString(type_def.Name));
            }

            throw new InvalidDataException("Unsupported attribute constructor kind: " + attr.Constructor.Kind);
        }

        internal static bool ConsumeFlag(this ParameterAttributes flag, ref ParameterAttributes attrs)
        {
            if ((attrs & flag) == flag)
            {
                attrs &= ~flag;
                return true;
            }

            return false;
        }

        internal static string Json(this bool value) => value ? "true" : "false";

        internal static string JsonString<T>(this T? value) => (value == null) ? "null" : Fmt.In($"\"{value}\"");

        internal static string ToJsonStringElements<T>(this T[] elements)
        {
            if (elements.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            string prefix = string.Empty;
            foreach (T element in elements)
            {
                builder.Append(Fmt.In($"{prefix}\"{element}\""));
                prefix = ",";
            }

            return builder.ToString();
        }

        internal static string ReadConstValue(this Constant constant, MetadataReader mr)
        {
            if (!constant.Value.IsNil)
            {
                return constant.TypeCode.ReadConstValue(mr.GetBlobReader(constant.Value));
            }

            return constant.TypeCode switch
            {
                ConstantTypeCode.Boolean => throw Violation.Data(),
                ConstantTypeCode.Char => throw Violation.Data(),
                ConstantTypeCode.SByte => throw Violation.Data(),
                ConstantTypeCode.Byte => throw Violation.Data(),
                ConstantTypeCode.Int16 => throw Violation.Data(),
                ConstantTypeCode.UInt16 => throw Violation.Data(),
                ConstantTypeCode.Int32 => throw Violation.Data(),
                ConstantTypeCode.UInt32 => throw Violation.Data(),
                ConstantTypeCode.Int64 => throw Violation.Data(),
                ConstantTypeCode.UInt64 => throw Violation.Data(),
                ConstantTypeCode.Single => throw Violation.Data(),
                ConstantTypeCode.Double => throw Violation.Data(),
                ConstantTypeCode.String => "null",
                ConstantTypeCode.NullReference => throw Violation.Data(),
                _ => throw new InvalidOperationException(),
            };
        }

        private static string ReadConstValue(this ConstantTypeCode code, BlobReader blobReader)
        {
            return code switch
            {
                ConstantTypeCode.Boolean => blobReader.ReadBoolean().Json(),
                ConstantTypeCode.Char => Fmt.In($"'{blobReader.ReadChar()}'"),
                ConstantTypeCode.SByte => Fmt.In($"{blobReader.ReadSByte()}"),
                ConstantTypeCode.Byte => Fmt.In($"{blobReader.ReadByte()}"),
                ConstantTypeCode.Int16 => Fmt.In($"{blobReader.ReadInt16()}"),
                ConstantTypeCode.UInt16 => Fmt.In($"{blobReader.ReadUInt16()}"),
                ConstantTypeCode.Int32 => Fmt.In($"{blobReader.ReadInt32()}"),
                ConstantTypeCode.UInt32 => Fmt.In($"{blobReader.ReadUInt32()}"),
                ConstantTypeCode.Int64 => Fmt.In($"{blobReader.ReadInt64()}"),
                ConstantTypeCode.UInt64 => Fmt.In($"{blobReader.ReadUInt64()}"),
                ConstantTypeCode.Single => GetFloat(blobReader.ReadSingle()),
                ConstantTypeCode.Double => GetDouble(blobReader.ReadDouble()),
                ConstantTypeCode.String => GetString(blobReader.ReadConstant(ConstantTypeCode.String)),
                ConstantTypeCode.NullReference => "null",
                _ => throw new InvalidOperationException(),
            };
            static string GetString(object? obj)
            {
                return Fmt.In($"\"{(string)obj!}\"");
            }

            static string GetFloat(float value)
            {
                return
                    float.IsPositiveInfinity(value) ? "\"inf\"" :
                    float.IsNegativeInfinity(value) ? "\"-inf\"" :
                    float.IsNaN(value) ? "\"nan\"" :
                    Fmt.In($"{value}");
            }

            static string GetDouble(double value)
            {
                return
                    double.IsPositiveInfinity(value) ? "\"inf\"" :
                    double.IsNegativeInfinity(value) ? "\"-inf\"" :
                    double.IsNaN(value) ? "\"nan\"" :
                    Fmt.In($"{value}");
            }
        }
    }

    internal static class Violation
    {
        internal static InvalidDataException Data(string? optional_msg = null)
        {
            string suffix = (optional_msg == null) ? string.Empty : (": " + optional_msg);
            throw new InvalidDataException("an assumption about the win32metadata winmd data was violated" + suffix);
        }

        internal static InvalidDataException Patch(string? optional_msg = null)
        {
            string suffix = (optional_msg == null) ? string.Empty : (": " + optional_msg);
            throw new InvalidDataException("an error occurred while applying a patch" + suffix);
        }
    }

    internal static class Enforce
    {
        internal static void Invariant(bool invariant, string? optional_msg = null)
        {
            if (!invariant)
            {
                string suffix = (optional_msg == null) ? string.Empty : (": " + optional_msg);
                throw new InvalidOperationException("an invariant was violated" + suffix);
            }
        }

        internal static void Data(bool assumption, string? optional_msg = null)
        {
            if (!assumption)
            {
                throw Violation.Data(optional_msg);
            }
        }

        internal static void Patch(bool assumption, string? optional_msg = null)
        {
            if (!assumption)
            {
                throw Violation.Patch(optional_msg);
            }
        }

        // assert that something is true temporarily
        internal static void Temp(bool cond)
        {
            if (!cond)
            {
                throw new NotImplementedException("Enforce.Temp failed");
            }
        }

        internal static void AttrFixedArgCount(NamespaceAndName name, CustomAttributeValue<CustomAttrType> args, uint expected)
        {
            if (args.FixedArguments.Length != expected)
            {
                throw new InvalidDataException(Fmt.In(
                    $"expected attribute '{name.Name}' to have {expected} fixed arguments but got {args.FixedArguments.Length}"));
            }
        }

        internal static void AttrNamedArgCount(NamespaceAndName name, CustomAttributeValue<CustomAttrType> args, uint expected)
        {
            if (args.NamedArguments.Length != expected)
            {
                throw new InvalidDataException(Fmt.In(
                    $"expected attribute '{name.Name}' to have {expected} named arguments but got {args.NamedArguments.Length}"));
            }
        }

        internal static void NamedArgName(NamespaceAndName name, CustomAttributeValue<CustomAttrType> args, string expected, int index)
        {
            string? actual = args.NamedArguments[index].Name;
            Enforce.Data(actual == expected, Fmt.In(
                $"expected attribute '{name}' to have named argument at index {index} to be named '{expected}' but got '{actual}'"));
        }

        internal static T FixedAttrAs<T>(CustomAttributeTypedArgument<CustomAttrType> attr_value)
        {
            CustomAttrType expectedType = CustomAttrTypeMap.FromType(typeof(T));
            if (attr_value.Type == expectedType)
            {
                return (T)attr_value.Value!;
            }

            throw new InvalidDataException(Fmt.In($"expected attribute value to be '{expectedType}', but got '{attr_value.Type}'"));
        }

        internal static T NamedAttrAs<T>(CustomAttributeNamedArgument<CustomAttrType> attr_value)
        {
            CustomAttrType expectedType = CustomAttrTypeMap.FromType(typeof(T));
            if (attr_value.Type == expectedType)
            {
                return (T)attr_value.Value!;
            }

            throw new InvalidDataException(Fmt.In($"expected attribute value to be '{expectedType}', but got '{attr_value.Type}'"));
        }
    }
}
