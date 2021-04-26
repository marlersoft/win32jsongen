// <copyright file="TypeRefDecoder.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Reflection.Metadata;

    // Implements the ISignatureTypeProvider interface used as a callback by MetadataReader to create objects that represent types.
    internal class TypeRefDecoder : ISignatureTypeProvider<TypeRef, INothing?>
    {
        private readonly Dictionary<string, Api> apiNamespaceMap;
        private readonly Dictionary<TypeDefinitionHandle, TypeGenInfo> typeMap;

        internal TypeRefDecoder(Dictionary<string, Api> apiNamespaceMap, Dictionary<TypeDefinitionHandle, TypeGenInfo> typeMap)
        {
            this.apiNamespaceMap = apiNamespaceMap;
            this.typeMap = typeMap;
        }

        public TypeRef GetArrayType(TypeRef from, ArrayShape shape) => new TypeRef.ArrayOf(from, shape);

        public TypeRef GetPointerType(TypeRef from) => new TypeRef.PointerTo(from);

        public TypeRef GetPrimitiveType(PrimitiveTypeCode typeCode) => TypeRef.Primitive.Get(typeCode);

        public TypeRef GetTypeFromDefinition(MetadataReader mr, TypeDefinitionHandle handle, byte rawTypeKind) => new TypeRef.User(this.typeMap[handle].RefInfo);

        public TypeRef GetByReferenceType(TypeRef from) => throw Violation.Data();

        public TypeRef GetFunctionPointerType(MethodSignature<TypeRef> signature) => throw Violation.Data();

        public TypeRef GetGenericInstantiation(TypeRef genericType, ImmutableArray<TypeRef> typeArguments) => throw Violation.Data();

        public TypeRef GetGenericMethodParameter(INothing? genericContext, int index) => throw Violation.Data();

        public TypeRef GetGenericTypeParameter(INothing? genericContext, int index) => throw Violation.Data();

        public TypeRef GetModifiedType(TypeRef modifier, TypeRef unmodifiedType, bool isRequired) => throw Violation.Data();

        public TypeRef GetPinnedType(TypeRef elementType) => throw Violation.Data();

        public TypeRef GetSZArrayType(TypeRef elementType) => throw Violation.Data();

        public TypeRef GetTypeFromSpecification(MetadataReader mr, INothing? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => throw Violation.Data();

        public TypeRef GetTypeFromReference(MetadataReader mr, TypeReferenceHandle handle, byte rawTypeKind) => this.GetTypeFromReference(mr, handle);

        public TypeRef GetTypeFromReference(MetadataReader mr, TypeReferenceHandle handle)
        {
            var typeRef = mr.GetTypeReference(handle);
            string @namespace = mr.GetString(typeRef.Namespace);
            string name = mr.GetString(typeRef.Name);

            Enforce.Data(!typeRef.ResolutionScope.IsNil);
            if (typeRef.ResolutionScope.Kind == HandleKind.ModuleDefinition)
            {
                return this.GetTypeFromNamespaceAndNameInThisModule(@namespace, name);
            }
            else if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                TypeRefInfo enclosingTypeRef = this.ResolveNestedType(
                    mr,
                    (TypeReferenceHandle)typeRef.ResolutionScope,
                    new NestedTypePath(name, null));
                Enforce.Data(@namespace.Length == 0, "expected nested type to have empty namespace");
                return new TypeRef.User(enclosingTypeRef);
            }
            else if (typeRef.ResolutionScope.Kind == HandleKind.AssemblyReference)
            {
                if (@namespace == "System")
                {
                    if (name == "Guid")
                    {
                        return TypeRef.Guid.Instance;
                    }
                }
                else if (@namespace == "Windows.System")
                {
                    // Looks like this may be defined in another metadata binary?
                    //    https://github.com/microsoft/win32metadata/issues/126
                    if (name == "DispatcherQueueController")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }
                }
                else if (@namespace == "Windows.Foundation")
                {
                    if (name == "IPropertyValue")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }
                }
                else if (@namespace == "Windows.Graphics.Effects")
                {
                    if (name == "IGraphicsEffectSource")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }
                }
                else if (@namespace == "Windows.UI.Composition")
                {
                    if (name == "ICompositionSurface")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }

                    if (name == "CompositionGraphicsDevice")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }

                    if (name == "CompositionCapabilities")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }

                    if (name == "Compositor")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }
                }
                else if (@namespace == "Windows.UI.Composition.Desktop")
                {
                    if (name == "DesktopWindowTarget")
                    {
                        return new TypeRef.MissingClrType(@namespace, name);
                    }
                }

                throw new InvalidOperationException();
            }

            throw Violation.Data(Fmt.In($"unhandled typeRef.ResolutionScope.Kind {typeRef.ResolutionScope.Kind}"));
        }

        public TypeRef GetTypeFromNamespaceAndNameInThisModule(string @namespace, string name)
        {
            var api = this.apiNamespaceMap[@namespace];
            return new TypeRef.User(api.TopLevelTypes.LookupRefInfoByFqn(api.TypeNameFqnMap[name]));
        }

        private static TypeGenInfo? TryResolveNestedTypePath(TypeGenInfo typeInfo, NestedTypePath nestedTypePath)
        {
            TypeGenInfo? nestedTypeInfo = typeInfo.TryGetNestedTypeByName(nestedTypePath.Name);
            if (nestedTypeInfo == null || nestedTypePath.Next == null)
            {
                return nestedTypeInfo;
            }

            return TryResolveNestedTypePath(nestedTypeInfo, nestedTypePath.Next);
        }

        private TypeRefInfo ResolveNestedType(MetadataReader mr, TypeReferenceHandle enoclosingTypeRefHandle, NestedTypePath nestedTypePath)
        {
            var enclosingTypeRef = mr.GetTypeReference(enoclosingTypeRefHandle);
            var @namespace = mr.GetString(enclosingTypeRef.Namespace);
            var enclosingTypeName = mr.GetString(enclosingTypeRef.Name);

            // TODO: file an issue for this?
            if (enclosingTypeName.EndsWith("____1", StringComparison.Ordinal) || enclosingTypeName.EndsWith("____2", StringComparison.Ordinal))
            {
                string newName = enclosingTypeName.Remove(enclosingTypeName.Length - 5);
                Console.WriteLine("!!! Interpreting '{0}' as '{1}'", enclosingTypeName, newName);
                enclosingTypeName = newName;
            }

            if (enclosingTypeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                return this.ResolveNestedType(
                    mr,
                    (TypeReferenceHandle)enclosingTypeRef.ResolutionScope,
                    new NestedTypePath(enclosingTypeName, nestedTypePath));
            }

            Enforce.Data(!enclosingTypeRef.ResolutionScope.IsNil);
            if (enclosingTypeRef.ResolutionScope.Kind == HandleKind.ModuleDefinition)
            {
                Api api = this.apiNamespaceMap[@namespace];
                OneOrMore<TypeGenInfo> typeInfos = api.TopLevelTypes.LookupTypeInfosByFqn(api.TypeNameFqnMap[enclosingTypeName]);
                TypeGenInfo? nestedType = TryResolveNestedTypePath(typeInfos.First, nestedTypePath);
                foreach (TypeGenInfo typeInfo in typeInfos.Others)
                {
                    TypeGenInfo? nextNestedType = TryResolveNestedTypePath(typeInfo, nestedTypePath);
                    if (nextNestedType != null)
                    {
                        if (nestedType == null)
                        {
                            nestedType = nextNestedType;
                        }
                        else
                        {
                            Enforce.Data(nestedType.RefInfo.Equals(nextNestedType.RefInfo));
                        }
                    }
                }

                if (nestedType != null)
                {
                    return nestedType.RefInfo;
                }

                throw Violation.Data();
            }

            throw Violation.Data(Fmt.In($"unhandled typeRef.ResolutionScope.Kind {enclosingTypeRef.ResolutionScope.Kind}"));
        }

        internal class NestedTypePath
        {
            internal NestedTypePath(string name, NestedTypePath? next)
            {
                this.Name = name;
                this.Next = next;
            }

            internal string Name { get; }

            internal NestedTypePath? Next { get; }
        }
    }
}
