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

        public TypeRef GetTypeFromDefinition(MetadataReader mr, TypeDefinitionHandle handle, byte rawTypeKind) => new TypeRef.User(this.typeMap[handle]);

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
                TypeGenInfo enclosingTypeRef = this.ResolveEnclosingType(mr, (TypeReferenceHandle)typeRef.ResolutionScope);
                Enforce.Data(@namespace.Length == 0, "expected nested type to have empty namespace");
                return new TypeRef.User(enclosingTypeRef.GetNestedTypeByName(name));
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
            return new TypeRef.User(api.TopLevelTypes[api.TypeNameFqnMap[name]]);
        }

        private TypeGenInfo ResolveEnclosingType(MetadataReader mr, TypeReferenceHandle typeRefHandle)
        {
            var typeRef = mr.GetTypeReference(typeRefHandle);
            var @namespace = mr.GetString(typeRef.Namespace);
            var name = mr.GetString(typeRef.Name);

            Enforce.Data(!typeRef.ResolutionScope.IsNil);
            if (typeRef.ResolutionScope.Kind == HandleKind.ModuleDefinition)
            {
                Api api = this.apiNamespaceMap[@namespace];
                return api.TopLevelTypes[api.TypeNameFqnMap[name]];
            }

            if (typeRef.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                TypeGenInfo enclosingTypeRef = this.ResolveEnclosingType(mr, (TypeReferenceHandle)typeRef.ResolutionScope);
                Enforce.Data(@namespace.Length == 0, "expected nested type to have empty namespace");
                return enclosingTypeRef.GetNestedTypeByName(name);
            }

            throw Violation.Data(Fmt.In($"unhandled typeRef.ResolutionScope.Kind {typeRef.ResolutionScope.Kind}"));
        }
    }
}
