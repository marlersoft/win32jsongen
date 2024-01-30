// <copyright file="TypeGenInfo.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>
#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1201 // Elements should appear in the correct order

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection.Metadata;

    internal class TypeGenInfo
    {
        private List<TypeGenInfo>? nestedTypes;

        private TypeGenInfo(MetadataReader mr, TypeDefinition def, string apiName, string name, string apiNamespace, string fqn, TypeGenInfo? enclosingType)
        {
            this.Def = def;
            this.ApiNamespace = apiNamespace;
            this.EnclosingType = enclosingType;
            this.IsNativeTypedef = false;

            Enforce.Data(def.GetDeclarativeSecurityAttributes().Count == 0);
            Enforce.Data(def.GetEvents().Count == 0);
            Enforce.Data(def.GetGenericParameters().Count == 0);
            Enforce.Data(def.GetMethodImplementations().Count == 0);
            Enforce.Data(def.GetProperties().Count == 0);

            TypeRefKind typeRefTargetKind;
            if (def.BaseType.IsNil)
            {
                this.BaseTypeName = new NamespaceAndName(string.Empty, string.Empty);
                typeRefTargetKind = TypeRefKind.Com;
            }
            else
            {
                Enforce.Data(def.GetInterfaceImplementations().Count == 0);
                Enforce.Data(def.BaseType.Kind == HandleKind.TypeReference);
                TypeReference baseTypeRef = mr.GetTypeReference((TypeReferenceHandle)def.BaseType);
                this.BaseTypeName = new NamespaceAndName(mr.GetString(baseTypeRef.Namespace), mr.GetString(baseTypeRef.Name));
                if (this.BaseTypeName == new NamespaceAndName("System", "MulticastDelegate"))
                {
                    typeRefTargetKind = TypeRefKind.FunctionPointer;
                }
                else
                {
                    typeRefTargetKind = TypeRefKind.Default;
                }

                if (this.BaseTypeName == new NamespaceAndName("System", "Enum"))
                {
                    this.IsNativeTypedef = true;
                }
            }

            foreach (CustomAttributeHandle attrHandle in def.GetCustomAttributes())
            {
                if (CustomAttr.Decode(mr, attrHandle) is CustomAttr.Const.NativeTypedef)
                {
                    this.IsNativeTypedef = true;
                    break;
                }
            }

            this.RefInfo = new TypeRefInfo(
                ApiName: apiName,
                Name: name,
                Fqn: fqn,
                IsNativeTypedef: this.IsNativeTypedef,
                ParentTypeQualifier: (enclosingType == null) ? ParentTypeQualifier.Root : enclosingType.ParentTypeQualifier.Add(name),
                TypeRefTargetKind: typeRefTargetKind);
        }

        public enum TypeRefKind
        {
            Default,
            FunctionPointer,
            Com,
        }

        internal TypeRefInfo RefInfo { get; }

        internal TypeDefinition Def { get; }

        internal string ApiName { get => this.RefInfo.ApiName; }

        internal string Name { get => this.RefInfo.Name; }

        internal string ApiNamespace { get; }

        internal string Fqn { get => this.RefInfo.Fqn; } // note: all fqn (fully qualified name)'s are unique

        internal ParentTypeQualifier ParentTypeQualifier { get => this.RefInfo.ParentTypeQualifier; }

        internal TypeGenInfo? EnclosingType { get; }

        internal NamespaceAndName BaseTypeName { get; }

        internal TypeRefKind TypeRefTargetKind { get => this.RefInfo.TypeRefTargetKind; }

        internal bool IsNativeTypedef { get; }

        internal bool IsNested
        {
            get
            {
                return this.EnclosingType != null;
            }
        }

        internal uint NestedTypeCount
        {
            get
            {
                return (this.nestedTypes == null) ? 0 : (uint)this.nestedTypes.Count;
            }
        }

        internal IEnumerable<TypeGenInfo> NestedTypesEnumerable
        {
            get
            {
                return (this.nestedTypes == null) ? Enumerable.Empty<TypeGenInfo>() : this.nestedTypes;
            }
        }

        internal static TypeGenInfo CreateNotNested(MetadataReader mr, TypeDefinition def, string name, string @namespace, Dictionary<string, string> apiNamespaceToName)
        {
            Enforce.Invariant(!def.IsNested, "CreateNotNested called for TypeDefinition that is nested");
            string? apiName;
            if (!apiNamespaceToName.TryGetValue(@namespace, out apiName))
            {
                Enforce.Data(@namespace.StartsWith(Metadata.WindowsWin32NamespacePrefix, StringComparison.Ordinal));
                apiName = @namespace.Substring(Metadata.WindowsWin32NamespacePrefix.Length);
                apiNamespaceToName.Add(@namespace, apiName);
            }

            string fqn = Fmt.In($"{@namespace}.{name}");
            return new TypeGenInfo(
                mr: mr,
                def: def,
                apiName: apiName,
                name: name,
                apiNamespace: @namespace,
                fqn: fqn,
                enclosingType: null);
        }

        internal static TypeGenInfo CreateNested(MetadataReader mr, TypeDefinition def, TypeGenInfo enclosingType)
        {
            Enforce.Invariant(def.IsNested, "CreateNested called for TypeDefinition that is not nested");
            string name = mr.GetString(def.Name);
            string @namespace = mr.GetString(def.Namespace);
            Enforce.Data(@namespace.Length == 0, "I thought all nested types had an empty namespace");
            string fqn = Fmt.In($"{enclosingType.Fqn}+{name}");
            return new TypeGenInfo(
                mr: mr,
                def: def,
                apiName: enclosingType.ApiName,
                name: name,
                apiNamespace: enclosingType.ApiNamespace,
                fqn: fqn,
                enclosingType: enclosingType);
        }

        internal TypeGenInfo? TryGetNestedTypeByName(string name)
        {
            if (this.nestedTypes != null)
            {
                foreach (TypeGenInfo info in this.nestedTypes)
                {
                    if (info.Name == name)
                    {
                        return info;
                    }
                }
            }

            return null;
        }

        internal void AddNestedType(TypeGenInfo type_info)
        {
            if (this.nestedTypes == null)
            {
                this.nestedTypes = new List<TypeGenInfo>();
            }
            else if (this.TryGetNestedTypeByName(type_info.Name) != null)
            {
                throw new InvalidOperationException(Fmt.In($"nested type '{type_info.Name}' already exists in '{this.Fqn}'"));
            }

            this.nestedTypes.Add(type_info);
        }

        internal TypeGenInfo GetNestedTypeByName(string name) => this.TryGetNestedTypeByName(name) is TypeGenInfo info ? info :
                throw new ArgumentException(Fmt.In($"type '{this.Fqn}' does not have nested type '{name}'"));

        internal bool HasNestedType(TypeGenInfo typeInfo)
        {
            Enforce.Invariant(typeInfo.IsNested);
            foreach (TypeGenInfo nestedTypeInfo in this.NestedTypesEnumerable)
            {
                if (object.ReferenceEquals(nestedTypeInfo, typeInfo))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal record TypeRefInfo(
        string ApiName,
        string Name,
        string Fqn,
        bool IsNativeTypedef,
        ParentTypeQualifier ParentTypeQualifier,
        TypeGenInfo.TypeRefKind TypeRefTargetKind);

    internal class ParentTypeQualifier
    {
        public static readonly ParentTypeQualifier Root = new ParentTypeQualifier(null, Array.Empty<string>());

        private readonly ParentTypeQualifier parent;
        private readonly string[] names;
        private Dictionary<string, ParentTypeQualifier>? children;

        private ParentTypeQualifier(ParentTypeQualifier? parent, string[] names)
        {
            this.parent = parent ?? this;
            this.names = names;
        }

        public string[] Qualifiers { get => this.parent.names; }

        public ParentTypeQualifier Add(string name)
        {
            if (this.children != null && this.children.TryGetValue(name, out ParentTypeQualifier? existing))
            {
                return existing;
            }

            if (this.children == null)
            {
                this.children = new Dictionary<string, ParentTypeQualifier>();
            }

            ParentTypeQualifier q = new ParentTypeQualifier(this, this.names.Concat(new string[] { name }).ToArray());
            this.children.Add(name, q);
            return q;
        }
    }
}
