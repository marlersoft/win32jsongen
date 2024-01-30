// <copyright file="JsonGenerator.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Reflection.Metadata;
    using System.Text;

    internal class JsonGenerator
    {
        private readonly MetadataReader mr;
        private readonly Dictionary<string, Api> apiNamespaceMap = new Dictionary<string, Api>();
        private readonly Dictionary<TypeDefinitionHandle, TypeGenInfo> typeMap = new Dictionary<TypeDefinitionHandle, TypeGenInfo>();
        private readonly TypeRefDecoder typeRefDecoder;

        private JsonGenerator(MetadataReader mr)
        {
            this.mr = mr;

            Dictionary<string, string> apiNamespaceToName = new Dictionary<string, string>();

            // ---------------------------------------------------------------
            // Scan all types and sort into the api they belong to
            // ---------------------------------------------------------------
            List<TypeDefinitionHandle> nestedTypes = new List<TypeDefinitionHandle>();
            foreach (TypeDefinitionHandle typeDefHandle in this.mr.TypeDefinitions)
            {
                TypeDefinition typeDef = mr.GetTypeDefinition(typeDefHandle);

                // skip nested types until we get all the non-nested types, this is because
                // we need to be able to look up the enclosing type to get all the info we need
                if (typeDef.IsNested)
                {
                    nestedTypes.Add(typeDefHandle);
                    continue;
                }

                string typeName = mr.GetString(typeDef.Name);
                string typeNamespace = mr.GetString(typeDef.Namespace);
                if (typeNamespace.Length == 0)
                {
                    Enforce.Data(typeName == "<Module>", "found a type without a namespace that is not nested and not '<Module>'");
                    continue;
                }

                Api? api;
                if (!this.apiNamespaceMap.TryGetValue(typeNamespace, out api))
                {
                    api = new Api(typeNamespace);
                    this.apiNamespaceMap.Add(typeNamespace, api);
                }

                // The "Apis" type is a specially-named type reserved to contain all the constant
                // and function declarations for an api.
                if (typeName == "Apis")
                {
                    Enforce.Data(api.Constants == null, "multiple Apis types in the same namespace");
                    api.Constants = typeDef.GetFields();
                    api.Funcs = typeDef.GetMethods();
                }
                else
                {
                    TypeGenInfo typeInfo = TypeGenInfo.CreateNotNested(mr, typeDef, typeName, typeNamespace, apiNamespaceToName);
                    this.typeMap.Add(typeDefHandle, typeInfo);
                    api.AddTopLevelType(typeInfo);
                }
            }

            // ---------------------------------------------------------------
            // Now go back through and create objects for the nested types
            // ---------------------------------------------------------------
            for (uint pass = 1; ; pass++)
            {
                int saveCount = nestedTypes.Count;
                Console.WriteLine("DEBUG: nested loop pass {0} (types left: {1})", pass, saveCount);

                for (int i = nestedTypes.Count - 1; i >= 0; i--)
                {
                    TypeDefinitionHandle typeDefHandle = nestedTypes[i];
                    TypeDefinition typeDef = mr.GetTypeDefinition(typeDefHandle);
                    Enforce.Invariant(typeDef.IsNested);
                    if (this.typeMap.TryGetValue(typeDef.GetDeclaringType(), out TypeGenInfo? enclosingType))
                    {
                        TypeGenInfo typeInfo = TypeGenInfo.CreateNested(mr, typeDef, enclosingType);
                        this.typeMap.Add(typeDefHandle, typeInfo);
                        enclosingType.AddNestedType(typeInfo);
                        nestedTypes.RemoveAt(i);
                        i--;
                    }
                }

                if (nestedTypes.Count == 0)
                {
                    break;
                }

                if (saveCount == nestedTypes.Count)
                {
                    throw new InvalidDataException(Fmt.In(
                        $"found {nestedTypes.Count} nested types whose declaring type handle does not match any type definition handle"));
                }
            }

            this.typeRefDecoder = new TypeRefDecoder(this.apiNamespaceMap, this.typeMap);
        }

        private enum FuncKind
        {
            Fixed,
            Ptr,
            Com,
        }

        internal static void Generate(MetadataReader mr, string outDir)
        {
            JsonGenerator generator = new JsonGenerator(mr);

            Dictionary<string, ApiPatch> apiMap = PatchConfig.CreateApiMap();

            foreach (Api api in generator.apiNamespaceMap.Values)
            {
                string filepath = Path.Combine(outDir, api.Name + ".json");
                using var fileStream = new FileStream(filepath, FileMode.Create, FileAccess.Write, FileShare.Read);

                // Encode the stream as UTF-8 without adding the byte order mark.
                using var streamWriter = new StreamWriter(fileStream, new UTF8Encoding(false));
                var writer = new TabWriter(streamWriter);
                Console.WriteLine("Api: {0}", api.Name);
                ApiPatch apiPatch = apiMap.GetValueOrDefault(api.Name, Patch.EmptyApi);
                apiPatch.ApplyCount += 1;
                generator.GenerateApi(writer, apiPatch, api);
            }

            foreach (KeyValuePair<string, ApiPatch> apiPatchPair in apiMap)
            {
                string apiName = apiPatchPair.Key;
                Enforce.Patch(
                    apiPatchPair.Value.ApplyCount > 0,
                    Fmt.In($"ApiPatch for '{apiName}' has not been applied, has this Api been removed?"));
                apiPatchPair.Value.SelectSubPatches(patch =>
                {
                    Enforce.Patch(
                        patch.ApplyCount > 0,
                        Fmt.In($"In Api '{apiName}', patch '{patch}' has not been applied, maybe it has been fixed?"));
                });
            }
        }

#pragma warning disable SA1513 // Closing brace should be followed by blank line
        private static void WriteJsonArray(TabWriter writer, string prefix, List<string> jsonElements, string suffix)
        {
            if (jsonElements == null || jsonElements.Count == 0)
            {
                writer.WriteLine("{0}[]{1}", prefix, suffix);
            }
            else
            {
                writer.WriteLine("{0}[", prefix);
                writer.Tab();
                string elementPrefix = string.Empty;
                foreach (string jsonElement in jsonElements)
                {
                    writer.WriteLine("{0}{1}", elementPrefix, jsonElement);
                    elementPrefix = ",";
                }
                writer.Untab();
                writer.WriteLine("]{0}", suffix);
            }
        }

        private void GenerateApi(TabWriter writer, ApiPatch apiPatch, Api api)
        {
            writer.WriteLine("{");
            writer.WriteLine();
            writer.WriteLine("\"Constants\":[");
            if (api.Constants != null)
            {
                string fieldPrefix = string.Empty;
                foreach (FieldDefinitionHandle fieldDef in api.Constants)
                {
                    writer.Tab();
                    this.GenerateConst(writer, apiPatch, fieldPrefix, fieldDef);
                    writer.Untab();
                    fieldPrefix = ",";
                }
            }
            writer.WriteLine("]");
            var unicodeSet = new UnicodeAliasSet();
            writer.WriteLine();
            writer.WriteLine(",\"Types\":[");
            {
                HashSet<string> typesRegistered = new HashSet<string>();
                string fieldPrefix = string.Empty;
                foreach (TypeGenInfo typeInfo in api.TopLevelTypes)
                {
                    TypePatch typePatch = apiPatch.TypeMap.GetValueOrDefault(typeInfo.Name, Patch.EmptyType);
                    typePatch.ApplyCount += 1;

                    if (typePatch.Config.Remove)
                    {
                        Console.WriteLine("Skipping '{0}' because it has been removed by a patch", typeInfo.Fqn);
                        continue;
                    }

                    writer.Tab();
                    this.GenerateType(writer, typePatch, fieldPrefix, typeInfo);
                    writer.Untab();
                    fieldPrefix = ",";

                    if (!typesRegistered.Contains(typeInfo.Name))
                    {
                        typesRegistered.Add(typeInfo.Name);
                        unicodeSet.RegisterTopLevelSymbol(typeInfo.Name);
                    }
                }
            }
            writer.WriteLine("]");
            writer.WriteLine();
            writer.WriteLine(",\"Functions\":[");
            if (api.Funcs != null)
            {
                HashSet<string> funcsRegistered = new HashSet<string>();
                string fieldPrefix = string.Empty;
                foreach (MethodDefinitionHandle funcHandle in api.Funcs)
                {
                    writer.Tab();
                    var generatedFuncName = this.GenerateFunc(writer, apiPatch, fieldPrefix, funcHandle, FuncKind.Fixed);
                    writer.Untab();
                    fieldPrefix = ",";
                    if (!funcsRegistered.Contains(generatedFuncName))
                    {
                        funcsRegistered.Add(generatedFuncName);
                        unicodeSet.RegisterTopLevelSymbol(generatedFuncName);
                    }
                }
            }
            writer.WriteLine("]");

            // NOTE: the win32metadata project winmd file doesn't explicitly contain unicode aliases
            //       but it seems like a good thing to include.
            writer.WriteLine();
            writer.WriteLine(",\"UnicodeAliases\":[");
            writer.Tab();
            {
                string fieldPrefix = string.Empty;
                foreach (UnicodeAlias alias in unicodeSet.Candidates)
                {
                    if (alias.HaveAnsi && alias.HaveWide && !unicodeSet.NonCandidates.Contains(alias.Alias))
                    {
                        writer.WriteLine("{0}\"{1}\"", fieldPrefix, alias.Alias);
                        fieldPrefix = ",";
                    }
                }
            }
            writer.Untab();
            writer.WriteLine("]");
            writer.WriteLine();
            writer.WriteLine("}");
        }

        private void GenerateConst(TabWriter writer, ApiPatch apiPatch, string constFieldPrefix, FieldDefinitionHandle fieldDefHandle)
        {
            FieldDefinition fieldDef = this.mr.GetFieldDefinition(fieldDefHandle);
            string name = this.mr.GetString(fieldDef.Name);

            ConstPatch constPatch = apiPatch.ConstMap.GetValueOrDefault(name, Patch.EmptyConst);
            if (constPatch.Config.Duplicated)
            {
                if (constPatch.ApplyCount == 1)
                {
                    return;
                }
            }
            constPatch.ApplyCount += 1;

            writer.WriteLine("{0}{{", constFieldPrefix);
            writer.Tab();
            using var defer = Defer.Do(() =>
            {
                writer.Untab();
                writer.WriteLine("}");
            });

            bool hasValue;
            if (fieldDef.Attributes == (FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault))
            {
                hasValue = true;
            }
            else if (fieldDef.Attributes == (FieldAttributes.Public | FieldAttributes.Static))
            {
                hasValue = false;
            }
            else
            {
                throw new InvalidOperationException(Fmt.In(
                    $"Unexpected Constant FieldDefinition attributes '{fieldDef.Attributes}'"));
            }
            Enforce.Data(fieldDef.GetOffset() == -1);
            Enforce.Data(fieldDef.GetRelativeVirtualAddress() == 0);

            List<string> jsonAttributes = new List<string>();

            CustomAttr.Guid? optionalGuidValue = null;
            CustomAttr.ProperyKey? optionalPropertyKey = null;

            // TODO: what is fieldDef.GetMarshallingDescriptor?
            foreach (CustomAttributeHandle attrHandle in fieldDef.GetCustomAttributes())
            {
                CustomAttr attr = CustomAttr.Decode(this.mr, attrHandle);
                if (object.ReferenceEquals(attr, CustomAttr.Const.Instance))
                {
                    // we already assume "const" on all constant values where this matters (i.e. string literals)
                }
                else if (attr is CustomAttr.Obsolete obsolete)
                {
                    jsonAttributes.Add(Fmt.In($"{{\"Kind\":\"Obsolete\",\"Message\":\"{obsolete.Message}\"}}"));
                }
                else if (attr is CustomAttr.Guid guidValue)
                {
                    optionalGuidValue = guidValue;
                }
                else if (attr is CustomAttr.ProperyKey key)
                {
                    optionalPropertyKey = key;
                }
                else
                {
                    Violation.Data();
                }
            }

            writer.WriteLine("\"Name\":\"{0}\"", name);
            TypeRef constTypeRef = fieldDef.DecodeSignature(this.typeRefDecoder, null);
            writer.WriteLine(",\"Type\":{0}", constTypeRef.ToJson());
            if (optionalGuidValue != null)
            {
                Enforce.Data(!hasValue);
                Enforce.Data(optionalPropertyKey == null);
                writer.WriteLine(",\"ValueType\":\"String\"");
                writer.WriteLine(",\"Value\":\"{0}\"", optionalGuidValue.Value);
            }
            else if (optionalPropertyKey != null)
            {
                Enforce.Data(!hasValue);
                writer.WriteLine(",\"ValueType\":\"PropertyKey\"");
                writer.WriteLine(",\"Value\":{{\"Fmtid\":\"{0}\",\"Pid\":{1}}}", optionalPropertyKey.Fmtid, optionalPropertyKey.Pid);
            }
            else
            {
                Enforce.Data(hasValue);
                Constant constant = this.mr.GetConstant(fieldDef.GetDefaultValue());
                writer.WriteLine(",\"ValueType\":\"{0}\"", constant.TypeCode.ToPrimitiveTypeCode());
                writer.WriteLine(",\"Value\":{0}", constant.ReadConstValue(this.mr));
            }
            WriteJsonArray(writer, ",\"Attrs\":", jsonAttributes, string.Empty);
        }

        private void GenerateType(TabWriter writer, TypePatch typePatch, string typeFieldPrefix, TypeGenInfo typeInfo)
        {
            writer.WriteLine("{0}{{", typeFieldPrefix);
            writer.Tab();
            using var defer = Defer.Do(() =>
            {
                writer.Untab();
                writer.WriteLine("}");
            });
            writer.WriteLine("\"Name\":\"{0}\"", typeInfo.Name);
            Enforce.Data(typeInfo.Def.GetNestedTypes().Length == typeInfo.NestedTypeCount);
            foreach (TypeDefinitionHandle nestedTypeHandle in typeInfo.Def.GetNestedTypes())
            {
                Enforce.Data(typeInfo.HasNestedType(this.typeMap[nestedTypeHandle]));
            }

            DecodedTypeAttributes attrs = new DecodedTypeAttributes(typeInfo.Def.Attributes);
            TypeVisibility expectedVisibility = typeInfo.IsNested ? TypeVisibility.NestedPublic : TypeVisibility.Public;
            Enforce.Data(attrs.Visibility == expectedVisibility);
            Enforce.Data(typeInfo.Def.BaseType.IsNil == !attrs.IsSealed);
            Enforce.Data(typeInfo.Def.BaseType.IsNil == attrs.IsInterface);
            Enforce.Data(typeInfo.Def.BaseType.IsNil == attrs.IsAbstract);

            // TODO: do something with these attributes (they are no longer used)
            string? guid = null;
            string? freeFuncAttr = null;
            bool isNativeTypedef = false;
            bool isFlags = false;
            string? optionalSupportedOsPlatform = null;
            string? optionalAlsoUsableFor = null;
            Arch[] archLimits = Array.Empty<Arch>();
            bool scopedEnum = false;
            long? invalidHandleValue = null;
            bool isAgile = false;

            foreach (CustomAttributeHandle attrHandle in typeInfo.Def.GetCustomAttributes())
            {
                CustomAttr attr = CustomAttr.Decode(this.mr, attrHandle);
                if (attr is CustomAttr.Guid guidAttr)
                {
                    Enforce.Data(guid == null);
                    guid = guidAttr.Value;
                }
                else if (attr is CustomAttr.RaiiFree raiiAttr)
                {
                    Enforce.Data(freeFuncAttr == null);
                    freeFuncAttr = raiiAttr.FreeFunc;
                }
                else if (attr is CustomAttr.NativeTypedef)
                {
                    isNativeTypedef = true;
                }
                else if (attr is CustomAttr.Flags)
                {
                    Enforce.Data(typeInfo.BaseTypeName == new NamespaceAndName("System", "Enum"));
                    isFlags = true;
                }
                else if (attr is CustomAttr.UnmanagedFunctionPointer)
                {
                    // TODO: do something with this
                }
                else if (attr is CustomAttr.SupportedOSPlatform supportedOsPlatform)
                {
                    Enforce.Data(optionalSupportedOsPlatform == null);
                    optionalSupportedOsPlatform = supportedOsPlatform.PlatformName;
                }
                else if (attr is CustomAttr.AlsoUsableFor alsoUsableFor)
                {
                    Enforce.Data(optionalAlsoUsableFor == null);
                    optionalAlsoUsableFor = alsoUsableFor.OtherType;
                }
                else if (attr is CustomAttr.SupportedArchitecture supportedArch)
                {
                    Enforce.Data(archLimits.Length == 0);
                    archLimits = CustomAttr.GetArchLimit(supportedArch.ArchFlags);
                }
                else if (attr is CustomAttr.ScopedEnum)
                {
                    scopedEnum = true;
                }
                else if (attr is CustomAttr.InvalidHandleValue value)
                {
                    invalidHandleValue = value.Value;
                }
                else if (attr is CustomAttr.Agile)
                {
                    isAgile = true;
                }
                else
                {
                    Enforce.Data(false);
                }
            }
            writer.WriteLine(",\"Architectures\":[{0}]", archLimits.ToJsonStringElements());
            writer.WriteLine(",\"Platform\":{0}", optionalSupportedOsPlatform.JsonString());

            if (isNativeTypedef)
            {
                Enforce.Data(scopedEnum == false);
                Enforce.Data(typeInfo.TypeRefTargetKind == TypeGenInfo.TypeRefKind.Default);
                writer.WriteLine(",\"Kind\":\"NativeTypedef\"");
                writer.WriteLine(",\"AlsoUsableFor\":{0}", optionalAlsoUsableFor.JsonString());
                Enforce.Data(attrs.Layout == TypeLayoutKind.Sequential);
                Enforce.Data(typeInfo.Def.GetFields().Count == 1);
                FieldDefinition targetDef = this.mr.GetFieldDefinition(typeInfo.Def.GetFields().First());
                string targetDefJson = targetDef.DecodeSignature(this.typeRefDecoder, null).ToJson();
                writer.WriteLine(",\"Def\":{0}", targetDefJson);
                Enforce.Data(guid == null);
                writer.WriteLine(",\"FreeFunc\":{0}", freeFuncAttr.JsonString());
                Enforce.Data(typeInfo.Def.GetMethods().Count == 0);
                Enforce.Data(typeInfo.NestedTypeCount == 0);
                writer.WriteLine(",\"InvalidHandleValue\":{0}", (invalidHandleValue != null) ? invalidHandleValue : "null");
                Enforce.Data(!isAgile);
            }
            else if (typeInfo.Def.BaseType.IsNil)
            {
                Enforce.Data(scopedEnum == false);
                Enforce.Data(typeInfo.TypeRefTargetKind == TypeGenInfo.TypeRefKind.Com);
                Enforce.Data(attrs.Layout == TypeLayoutKind.Auto);
                Enforce.Data(freeFuncAttr == null);
                Enforce.Data(optionalAlsoUsableFor is null);
                Enforce.Data(invalidHandleValue == null);
                this.GenerateComType(writer, typePatch.ToComPatch(), typeInfo, guid, isAgile);
            }
            else if (typeInfo.BaseTypeName == new NamespaceAndName("System", "Enum"))
            {
                Enforce.Data(typeInfo.TypeRefTargetKind == TypeGenInfo.TypeRefKind.Default);
                Enforce.Data(guid == null);
                Enforce.Data(freeFuncAttr == null);
                Enforce.Data(optionalAlsoUsableFor is null);
                Enforce.Data(attrs.Layout == TypeLayoutKind.Auto);
                Enforce.Data(invalidHandleValue == null);
                Enforce.Data(!isAgile);
                this.GenerateEnum(writer, typeInfo, isFlags, scopedEnum);
            }
            else if (typeInfo.BaseTypeName == new NamespaceAndName("System", "ValueType"))
            {
                Enforce.Data(scopedEnum == false);
                Enforce.Data(typeInfo.TypeRefTargetKind == TypeGenInfo.TypeRefKind.Default);
                Enforce.Data(freeFuncAttr == null);
                Enforce.Data(optionalAlsoUsableFor is null);
                Enforce.Data(invalidHandleValue == null);
                Enforce.Data(!isAgile);
                if (guid == null || typePatch.Config.NotComClassID)
                {
                    this.GenerateStruct(writer, typePatch, typeInfo, attrs.Layout);
                }
                else
                {
                    Enforce.Data(attrs.Layout == TypeLayoutKind.Sequential);
                    writer.WriteLine(",\"Kind\":\"ComClassID\"");
                    writer.WriteLine(",\"Guid\":{0}", guid.JsonString());
                    TypeLayout layout = typeInfo.Def.GetLayout();
                    Enforce.Data(layout.IsDefault);
                    Enforce.Data(layout.Size == 0);
                    Enforce.Data(layout.PackingSize == 0);
                    Enforce.Data(typeInfo.Def.GetFields().Count == 0);
                    Enforce.Data(typeInfo.Def.GetMethods().Count == 0);
                    Enforce.Data(typeInfo.Def.GetNestedTypes().Length == 0);
                }
            }
            else if (typeInfo.BaseTypeName == new NamespaceAndName("System", "MulticastDelegate"))
            {
                Enforce.Data(scopedEnum == false);
                Enforce.Data(typeInfo.TypeRefTargetKind == TypeGenInfo.TypeRefKind.FunctionPointer);
                Enforce.Data(guid == null);
                Enforce.Data(freeFuncAttr == null);
                Enforce.Data(optionalAlsoUsableFor is null);
                Enforce.Data(invalidHandleValue == null);
                Enforce.Data(!isAgile);
                Enforce.Data(attrs.Layout == TypeLayoutKind.Auto);
                this.GenerateFunctionPointer(writer, typeInfo);
            }
            else
            {
                throw Violation.Data();
            }
        }

        private void GenerateNestedTypes(TabWriter writer, TypePatch enclosingTypePatch, TypeGenInfo typeInfo)
        {
            string nestedFieldPrefix = string.Empty;
            writer.WriteLine(",\"NestedTypes\":[");
            foreach (TypeGenInfo nestedType in typeInfo.NestedTypesEnumerable)
            {
                TypePatch nestedTypePatch = enclosingTypePatch.NestedTypeMap.GetValueOrDefault(nestedType.Name, Patch.EmptyType);
                nestedTypePatch.ApplyCount += 1;

                writer.Tab();
                this.GenerateType(writer, nestedTypePatch, nestedFieldPrefix, nestedType);
                writer.Untab();
                nestedFieldPrefix = ",";
            }
            writer.WriteLine("]");
        }

        private void GenerateComType(TabWriter writer, ComTypePatch comTypePatch, TypeGenInfo typeInfo, string? guid, bool isAgile)
        {
            Enforce.Data(typeInfo.Def.GetFields().Count == 0);

            writer.WriteLine(",\"Kind\":\"Com\"");
            writer.WriteLine(",\"Guid\":{0}", guid.JsonString());
            {
                List<string> attrs = new List<string>();
                if (isAgile)
                {
                    attrs.Add("\"Agile\"");
                }
                WriteJsonArray(writer, ",\"Attrs\":", attrs, string.Empty);
            }

            string interfaceJson = "null";
            if (typeInfo.Def.GetInterfaceImplementations().Count != 0)
            {
                Enforce.Data(typeInfo.Def.GetInterfaceImplementations().Count == 1);
                InterfaceImplementationHandle ifaceImplHandle = typeInfo.Def.GetInterfaceImplementations().First();
                InterfaceImplementation ifaceImpl = this.mr.GetInterfaceImplementation(ifaceImplHandle);
                Enforce.Data(ifaceImpl.GetCustomAttributes().Count == 0);
                Enforce.Data(ifaceImpl.Interface.Kind == HandleKind.TypeReference);
                TypeRef ifaceType = this.typeRefDecoder.GetTypeFromReference(this.mr, (TypeReferenceHandle)ifaceImpl.Interface);
                interfaceJson = ifaceType.ToJson();
            }
            writer.WriteLine(",\"Interface\":{0}", interfaceJson);
            writer.WriteLine(",\"Methods\":[");
            writer.Tab();
            string methodElementPrefix = string.Empty;
            foreach (MethodDefinitionHandle methodDefHandle in typeInfo.Def.GetMethods())
            {
                this.GenerateFunc(writer, comTypePatch, methodElementPrefix, methodDefHandle, FuncKind.Com);
                methodElementPrefix = ",";
            }
            writer.Untab();
            writer.WriteLine("]");
            Enforce.Data(typeInfo.NestedTypeCount == 0);
        }

        private void GenerateEnum(TabWriter writer, TypeGenInfo typeInfo, bool isFlags, bool isScoped)
        {
            writer.WriteLine(",\"Kind\":\"Enum\"");
            writer.WriteLine(",\"Flags\":{0}", isFlags.Json());
            writer.WriteLine(",\"Scoped\":{0}", isScoped.Json());
            writer.WriteLine(",\"Values\":[");
            writer.Tab();
            string valueElemPrefix = string.Empty;
            ConstantTypeCode? integerBase = null;
            foreach (FieldDefinitionHandle fieldDefHandle in typeInfo.Def.GetFields())
            {
                FieldDefinition fieldDef = this.mr.GetFieldDefinition(fieldDefHandle);
                string fieldName = this.mr.GetString(fieldDef.Name);
                if (fieldDef.Attributes == (FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName))
                {
                    Enforce.Data(fieldName == "value__");
                    continue;
                }
                Enforce.Data(fieldDef.Attributes == (
                    FieldAttributes.Public |
                    FieldAttributes.Static |
                    FieldAttributes.Literal |
                    FieldAttributes.HasDefault));
                Enforce.Data(fieldDef.GetCustomAttributes().Count == 0);
                Enforce.Data(fieldDef.GetOffset() == -1);
                Enforce.Data(fieldDef.GetRelativeVirtualAddress() == 0);
                Constant valueConstant = this.mr.GetConstant(fieldDef.GetDefaultValue());
                integerBase = integerBase.HasValue ? integerBase.Value : valueConstant.TypeCode;
                Enforce.Data(integerBase == valueConstant.TypeCode);
                string value = valueConstant.ReadConstValue(this.mr);
                writer.WriteLine("{0}{{\"Name\":\"{1}\",\"Value\":{2}}}", valueElemPrefix, fieldName, value);
                valueElemPrefix = ",";
            }
            writer.Untab();
            writer.WriteLine("]");
            string quotes = integerBase.HasValue ? "\"" : string.Empty;
            writer.WriteLine(",\"IntegerBase\":{0}{1}{2}", quotes, integerBase.HasValue ? integerBase.Value.ToPrimitiveTypeCode() : "null", quotes);
            Enforce.Data(typeInfo.Def.GetMethods().Count == 0);
            Enforce.Data(typeInfo.NestedTypeCount == 0);
        }

        private void GenerateStruct(TabWriter writer, TypePatch typePatch, TypeGenInfo typeInfo, TypeLayoutKind layoutKind)
        {
            string kind;
            if (layoutKind == TypeLayoutKind.Explicit)
            {
                kind = "Union";
            }
            else
            {
                Enforce.Data(layoutKind == TypeLayoutKind.Sequential);
                kind = "Struct";
            }
            writer.WriteLine(",\"Kind\":\"{0}\"", kind);
            TypeLayout layout = typeInfo.Def.GetLayout();
            writer.WriteLine(",\"Size\":{0}", layout.Size);
            writer.WriteLine(",\"PackingSize\":{0}", layout.PackingSize);
            List<string> constFields = new List<string>();
            writer.WriteLine(",\"Fields\":[");
            writer.Tab();
            string fieldElemPrefix = string.Empty;
            foreach (FieldDefinitionHandle fieldDefHandle in typeInfo.Def.GetFields())
            {
                FieldDefinition fieldDef = this.mr.GetFieldDefinition(fieldDefHandle);
                if (layoutKind == TypeLayoutKind.Explicit)
                {
                    Enforce.Data(fieldDef.GetOffset() == 0);
                }
                string fieldName = this.mr.GetString(fieldDef.Name);
                Enforce.Data(fieldDef.GetRelativeVirtualAddress() == 0);
                if (fieldDef.Attributes == (FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault))
                {
                    // I'm not sure whether the metadata intended to put constants inside types like this or if they
                    // should be moved to the special "Api" type.  If so, I'll have to put them somewhere, not sure where yet though.
                    // I could add a "Constants" subfield, but right now only 2 types have these const fields so it's not worth adding
                    // this extra field to every single type just to accomodate some const fields on a couple types.
                    // Maybe I should open a github issue about this?  Ask why these are the only 2 types using const fields.
                    Enforce.Data(typeInfo.Name == "WSDXML_NODE" || typeInfo.Name == "WhitePoint");
                    Enforce.Data(fieldDef.GetCustomAttributes().Count == 0);
                    Enforce.Data(fieldDef.GetOffset() == -1);
                    Constant constant = this.mr.GetConstant(fieldDef.GetDefaultValue());
                    string value = constant.ReadConstValue(this.mr);
                    constFields.Add(Fmt.In($"{constant.TypeCode} {fieldName} = {value}"));
                    continue;
                }

                Enforce.Data(fieldDef.Attributes == FieldAttributes.Public);

                // TODO: verify fieldDef.GetOffset()?
                TypeRef fieldType = fieldDef.DecodeSignature(this.typeRefDecoder, null);
                List<string> jsonAttributes = new List<string>();
                foreach (CustomAttributeHandle attrHandle in fieldDef.GetCustomAttributes())
                {
                    CustomAttr attr = CustomAttr.Decode(this.mr, attrHandle);
                    if (object.ReferenceEquals(attr, CustomAttr.Const.Instance))
                    {
                        jsonAttributes.Add("\"Const\"");
                    }
                    else if (attr is CustomAttr.NotNullTerminated)
                    {
                        jsonAttributes.Add("\"NotNullTerminated\"");
                    }
                    else if (attr is CustomAttr.NullNullTerminated)
                    {
                        jsonAttributes.Add("\"NullNullTerminated\"");
                    }
                    else if (attr is CustomAttr.NativeArrayInfo nativeArrayInfo)
                    {
                        fieldType = new TypeRef.LPArray(nativeArrayInfo, fieldType, this.typeRefDecoder);
                    }
                    else if (attr is CustomAttr.Obsolete obselete)
                    {
                        jsonAttributes.Add("\"Obselete\"");
                    }
                    else
                    {
                        Violation.Data();
                    }
                }

                bool optional = false;
                if (typePatch.FieldMap.TryGetValue(fieldName, out FieldPatch? patch))
                {
                    patch.ApplyCount += 1;
                    if (patch.Config.Optional)
                    {
                        Enforce.Patch(!optional, Fmt.In($"field '{fieldName}' Optional patch has been fixed"));
                        optional = true;
                    }
                }
                if (optional)
                {
                    jsonAttributes.Add("\"Optional\"");
                }

                string attrs = string.Join(",", jsonAttributes);
                string fieldTypeJson = fieldType.ToJson();
                writer.WriteLine("{0}{{\"Name\":\"{1}\",\"Type\":{2},\"Attrs\":[{3}]}}", fieldElemPrefix, fieldName, fieldTypeJson, attrs);
                fieldElemPrefix = ",";
            }
            writer.Untab();
            writer.WriteLine("]");
            if (constFields.Count > 0)
            {
                writer.WriteLine(
                    ",\"Comment\":\"This type has {0} const fields, not sure if it's supposed to: {1}\"",
                    constFields.Count,
                    string.Join(", ", constFields));
            }
            Enforce.Data(typeInfo.Def.GetMethods().Count == 0);
            this.GenerateNestedTypes(writer, typePatch, typeInfo);
        }

        private void GenerateFunctionPointer(TabWriter writer, TypeGenInfo typeInfo)
        {
            Enforce.Data(typeInfo.Def.GetFields().Count == 0);
            Enforce.Data(typeInfo.NestedTypeCount == 0);
            Enforce.Data(typeInfo.Def.GetMethods().Count == 2);

            bool firstMethod = true;
            MethodDefinitionHandle? funcMethodHandle = null;
            foreach (MethodDefinitionHandle methodHandle in typeInfo.Def.GetMethods())
            {
                if (firstMethod)
                {
                    firstMethod = false;
                    Enforce.Data(this.mr.GetString(this.mr.GetMethodDefinition(methodHandle).Name) == ".ctor");
                }
                else
                {
                    Enforce.Data(funcMethodHandle == null);
                    funcMethodHandle = methodHandle;
                }
            }
            Enforce.Data(funcMethodHandle != null);
            this.GenerateFuncCommon(writer, IFuncPatchMap.None, funcMethodHandle!.Value, FuncKind.Ptr);
        }

        private string GenerateFunc(TabWriter writer, IFuncPatchMap funcPatchMap, string funcFieldPrefix, MethodDefinitionHandle funcHandle, FuncKind kind)
        {
            writer.WriteLine("{0}{{", funcFieldPrefix);
            writer.Tab();
            using var defer = Defer.Do(() =>
            {
                writer.Untab();
                writer.WriteLine("}");
            });
            return this.GenerateFuncCommon(writer, funcPatchMap, funcHandle, kind);
        }

        private string GenerateFuncCommon(TabWriter writer, IFuncPatchMap funcPatchMap, MethodDefinitionHandle funcHandle, FuncKind kind)
        {
            MethodDefinition funcDef = this.mr.GetMethodDefinition(funcHandle);
            string funcName = string.Empty;

            if (kind == FuncKind.Ptr)
            {
                writer.WriteLine(",\"Kind\":\"FunctionPointer\"");
            }
            else
            {
                funcName = this.mr.GetString(funcDef.Name);
                writer.WriteLine("\"Name\":\"{0}\"", funcName);
            }

            FuncPatch funcPatch = (funcName.Length == 0) ? Patch.EmptyFunc : funcPatchMap.FuncMap.GetValueOrDefault(funcName, Patch.EmptyFunc);
            funcPatch.ApplyCount += 1;

            // Looks like right now all the functions have these same attributes
            var decodedAttrs = new DecodedMethodAttributes(funcDef.Attributes);
            bool preserveSig = false;
            Enforce.Data(decodedAttrs.MemberAccess == MemberAccess.Public);
            if (kind == FuncKind.Ptr)
            {
                Enforce.Data(!decodedAttrs.IsStatic);
                Enforce.Data(decodedAttrs.IsVirtual);
                Enforce.Data(!decodedAttrs.PInvokeImpl);
                Enforce.Data(decodedAttrs.NewSlot);
                Enforce.Data(funcDef.ImplAttributes == MethodImplAttributes.CodeTypeMask);
                Enforce.Data(!decodedAttrs.IsAbstract);
            }
            else if (kind == FuncKind.Com)
            {
                Enforce.Data(!decodedAttrs.IsStatic);
                Enforce.Data(decodedAttrs.IsVirtual);
                Enforce.Data(!decodedAttrs.PInvokeImpl);
                Enforce.Data(decodedAttrs.NewSlot);
                Enforce.Data(decodedAttrs.IsAbstract);
                preserveSig = funcDef.ImplAttributes switch
                {
                    0 => false,
                    MethodImplAttributes.PreserveSig => true,
                    _ => throw Violation.Data(),
                };
            }
            else
            {
                Enforce.Data(decodedAttrs.IsStatic);
                Enforce.Data(!decodedAttrs.IsVirtual);
                Enforce.Data(decodedAttrs.PInvokeImpl);
                Enforce.Data(!decodedAttrs.NewSlot);
                preserveSig = funcDef.ImplAttributes switch
                {
                    MethodImplAttributes.IL => false,
                    MethodImplAttributes.PreserveSig => true,
                    _ => throw Violation.Data(),
                };
                Enforce.Data(!decodedAttrs.IsAbstract);
            }
            Enforce.Data(!decodedAttrs.IsFinal);
            Enforce.Data(decodedAttrs.HideBySig);
            Enforce.Data(!decodedAttrs.CheckAccessOnOverride);

            string? optionalSupportedOsPlatform = null;
            Arch[] archLimits = Array.Empty<Arch>();
            bool doesNotReturn = false;
            foreach (CustomAttributeHandle attrHandle in funcDef.GetCustomAttributes())
            {
                CustomAttr attr = CustomAttr.Decode(this.mr, attrHandle);
                if (attr is CustomAttr.SupportedOSPlatform supportedOsPlatform)
                {
                    Enforce.Data(optionalSupportedOsPlatform is null);
                    optionalSupportedOsPlatform = supportedOsPlatform.PlatformName;
                }
                else if (attr is CustomAttr.SupportedArchitecture supportedArch)
                {
                    Enforce.Data(archLimits.Length == 0);
                    archLimits = CustomAttr.GetArchLimit(supportedArch.ArchFlags);
                }
                else if (attr is CustomAttr.DoesNotReturn)
                {
                    doesNotReturn = true;
                }
                else
                {
                    Violation.Data();
                }
            }
            Enforce.Data(funcDef.GetDeclarativeSecurityAttributes().Count == 0);

            MethodImport methodImport = funcDef.GetImport();
            var methodImportAttrs = new DecodedMethodImportAttributes(methodImport.Attributes);
            Enforce.Data(methodImportAttrs.CharSet == CharSet.None);
            Enforce.Data(methodImportAttrs.BestFit == null);
            Enforce.Data(methodImportAttrs.ThrowOnUnmapableChar == null);
            if (kind == FuncKind.Fixed)
            {
                Enforce.Data(methodImportAttrs.ExactSpelling);
                Enforce.Data(methodImportAttrs.CallConv == CallConv.Winapi);
                Enforce.Data(this.mr.GetString(methodImport.Name) == funcName);
            }
            else
            {
                Enforce.Data(!methodImportAttrs.ExactSpelling);
                Enforce.Data(methodImportAttrs.CallConv == CallConv.None);
                Enforce.Data(this.mr.GetString(methodImport.Name).Length == 0);
            }

            ModuleReference moduleRef = this.mr.GetModuleReference(methodImport.Module);
            Enforce.Data(moduleRef.GetCustomAttributes().Count == 0);
            string importName = (kind == FuncKind.Fixed) ? this.mr.GetString(moduleRef.Name) : string.Empty;

            MethodSignature<TypeRef> methodSig = funcDef.DecodeSignature(this.typeRefDecoder, null);

            Enforce.Data(methodSig.Header.Kind == SignatureKind.Method);
            Enforce.Data(methodSig.Header.CallingConvention == SignatureCallingConvention.Default);
            if (kind == FuncKind.Fixed)
            {
                Enforce.Data(methodSig.Header.Attributes == SignatureAttributes.None);
            }
            else
            {
                Enforce.Data(methodSig.Header.Attributes == SignatureAttributes.Instance);
            }

            //var remapSig = methodSig.ReturnType is TypeRef.User user && !user.Info.IsNativeTypedef;

            writer.WriteLine(",\"SetLastError\":{0}", methodImportAttrs.SetLastError.Json());
            if (kind == FuncKind.Fixed)
            {
                writer.WriteLine(",\"DllImport\":\"{0}\"", importName);
            }
            //if (remapSig)
            //{
            //    writer.WriteLine(",\"ReturnType\":{0}", TypeRef.Primitive.Void.ToJson());
            //    writer.WriteLine(",\"ReturnAttrs\":[]");
            //}
            //else
            {
                writer.WriteLine(",\"ReturnType\":{0}", methodSig.ReturnType.ToJson());
                {
                    List<string> returnJsonAttrs = new List<string>();
                    if (funcPatch.ReturnType != null)
                    {
                        funcPatch.ReturnType.ApplyCount += 1;
                        if (funcPatch.ReturnType.Config.Optional)
                        {
                            returnJsonAttrs.Add("\"Optional\"");
                        }
                    }
                    writer.WriteLine(",\"ReturnAttrs\":[{0}]", string.Join(",", returnJsonAttrs));
                }
            }
            if (kind != FuncKind.Ptr)
            {
                // When kind == FuncKind.Ptr, the Architectures/Platform will have already been printed in GenerateType
                writer.WriteLine(",\"Architectures\":[{0}]", archLimits.ToJsonStringElements());
                writer.WriteLine(",\"Platform\":{0}", optionalSupportedOsPlatform.JsonString());
            }

            List<string> funcJsonAttrs = new List<string>();
            if (decodedAttrs.SpecialName)
            {
                Enforce.Data(
                       funcName.StartsWith("get_", StringComparison.Ordinal)
                    || funcName.StartsWith("put_", StringComparison.Ordinal)
                    || funcName.StartsWith("add_", StringComparison.Ordinal)
                    || funcName.StartsWith("remove_", StringComparison.Ordinal));
                funcJsonAttrs.Add("\"SpecialName\"");
            }
            if (doesNotReturn)
            {
                Enforce.Data(methodSig.ReturnType == TypeRef.Primitive.Void);
                funcJsonAttrs.Add("\"DoesNotReturn\"");
            }
            if (preserveSig)
            {
                funcJsonAttrs.Add("\"PreserveSig\"");
            }
            WriteJsonArray(writer, ",\"Attrs\":", funcJsonAttrs, string.Empty);

            writer.WriteLine(",\"Params\":[");
            writer.Tab();
            var paramCount = 0;
            if (!funcPatch.Func.SkipParams)
            {
                paramCount = this.GenerateParams(writer, funcDef, funcPatch, methodSig);
            }
            //if (remapSig)
            //{
            //    if (funcPatch.ReturnType != null)
            //    {
            //        funcPatch.ReturnType.ApplyCount += 1;
            //        // ignore patch
            //    }
            //    string paramFieldPrefix = (paramCount > 0) ? "," : string.Empty;
            //    writer.WriteLine($"{paramFieldPrefix}{{\"Name\":\"retval\",\"Type\":{new TypeRef.PointerTo(methodSig.ReturnType).ToJson()},\"Attrs\":[\"Out\"]}}");
            //}
            writer.Untab();
            writer.WriteLine("]");
            return funcName;
        }

        private int GenerateParams(TabWriter writer, MethodDefinition funcDef, FuncPatch funcPatch, MethodSignature<TypeRef> methodSig)
        {
            string paramFieldPrefix = string.Empty;
            int count = 0;
            int nextExpectedSequenceNumber = 1;
            foreach (ParameterHandle paramHandle in funcDef.GetParameters())
            {
                Parameter param = this.mr.GetParameter(paramHandle);
                if (param.SequenceNumber == 0)
                {
                    // this is the return parameter
                    continue;
                }

                Enforce.Data(param.SequenceNumber == nextExpectedSequenceNumber, "parameters were not ordered");
                nextExpectedSequenceNumber++;

                // TODO: handle param.GetMarshallingDescriptor();
                Enforce.Data(param.GetDefaultValue().IsNil);
                List<string> jsonAttributes = new List<string>();
                ParameterAttributes remainingAttrs = param.Attributes;
                bool optional = false;
                if (ParameterAttributes.In.ConsumeFlag(ref remainingAttrs))
                {
                    jsonAttributes.Add("\"In\"");
                }
                if (ParameterAttributes.Out.ConsumeFlag(ref remainingAttrs))
                {
                    jsonAttributes.Add("\"Out\"");
                }
                if (ParameterAttributes.Optional.ConsumeFlag(ref remainingAttrs))
                {
                    optional = true; // set attribute below
                }
                Enforce.Data(remainingAttrs == ParameterAttributes.None);
                TypeRef paramType = methodSig.ParameterTypes[param.SequenceNumber - 1];
                bool @const = false;
                foreach (CustomAttributeHandle attrHandle in param.GetCustomAttributes())
                {
                    CustomAttr attr = CustomAttr.Decode(this.mr, attrHandle);
                    if (object.ReferenceEquals(attr, CustomAttr.Const.Instance))
                    {
                        @const = true; // set attribute below
                    }
                    else if (object.ReferenceEquals(attr, CustomAttr.ComOutPtr.Instance))
                    {
                        jsonAttributes.Add("\"ComOutPtr\"");
                    }
                    else if (attr is CustomAttr.NativeArrayInfo nativeArrayInfo)
                    {
                        paramType = new TypeRef.LPArray(nativeArrayInfo, paramType, this.typeRefDecoder);
                    }
                    else if (attr is CustomAttr.NotNullTerminated)
                    {
                        jsonAttributes.Add("\"NotNullTerminated\"");
                    }
                    else if (attr is CustomAttr.NullNullTerminated)
                    {
                        jsonAttributes.Add("\"NullNullTerminated\"");
                    }
                    else if (attr is CustomAttr.RetVal)
                    {
                        jsonAttributes.Add("\"RetVal\"");
                    }
                    else if (attr is CustomAttr.FreeWith freeWith)
                    {
                        jsonAttributes.Add(Fmt.In($"{{\"Kind\":\"FreeWith\",\"Func\":\"{freeWith.Name}\"}}"));
                    }
                    else if (attr is CustomAttr.MemorySize memorySize)
                    {
                        jsonAttributes.Add(Fmt.In($"{{\"Kind\":\"MemorySize\",\"BytesParamIndex\":{memorySize.BytesParamIndex}}}"));
                    }
                    else if (attr is CustomAttr.DoNotRelease)
                    {
                        jsonAttributes.Add("\"DoNotRelease\"");
                    }
                    else if (attr is CustomAttr.Reserved)
                    {
                        jsonAttributes.Add("\"Reserved\"");
                    }
                    else
                    {
                        Violation.Data();
                    }
                }

                string paramName = this.mr.GetString(param.Name);
                Enforce.Data(paramName.Length > 0);

                if (funcPatch.ParamMap.TryGetValue(paramName, out ParamPatch? patch))
                {
                    patch.ApplyCount += 1;
                    if (patch.Config.Optional)
                    {
                        Enforce.Patch(!optional, Fmt.In($"parameter '{paramName}' Optional patch has been fixed"));
                        optional = true;
                    }
                    if (patch.Config.Const)
                    {
                        Enforce.Patch(!@const, Fmt.In($"parameter '{paramName}' Const patch has been fixed"));
                        @const = true;
                    }
                }
                if (optional)
                {
                    jsonAttributes.Add("\"Optional\"");
                }
                if (@const)
                {
                    jsonAttributes.Add("\"Const\"");
                }

                string attrs = string.Join(",", jsonAttributes);
                writer.WriteLine($"{paramFieldPrefix}{{\"Name\":\"{paramName}\",\"Type\":{paramType.ToJson()},\"Attrs\":[{attrs}]}}");
                paramFieldPrefix = ",";
                count += 1;
            }

            return count;
        }

#pragma warning restore SA1513 // Closing brace should be followed by blank line

        private class UnicodeAlias
        {
            internal UnicodeAlias(string alias, string? ansi = null, string? wide = null, bool haveAnsi = false, bool haveWide = false)
            {
                this.Alias = alias;
                this.Ansi = (ansi != null) ? ansi : alias + "A";
                this.Wide = (wide != null) ? wide : alias + "W";
                this.HaveAnsi = haveAnsi;
                this.HaveWide = haveWide;
            }

            internal string Alias { get; }

            internal string Ansi { get; }

            internal string Wide { get; }

            internal bool HaveAnsi { get; set; }

            internal bool HaveWide { get; set; }
        }

        private class UnicodeAliasSet
        {
            private readonly Dictionary<string, UnicodeAlias> ansiMap = new Dictionary<string, UnicodeAlias>();
            private readonly Dictionary<string, UnicodeAlias> wideMap = new Dictionary<string, UnicodeAlias>();

            internal UnicodeAliasSet()
            {
            }

            internal HashSet<string> NonCandidates { get; } = new HashSet<string>();

            internal List<UnicodeAlias> Candidates { get; } = new List<UnicodeAlias>();

            internal void RegisterTopLevelSymbol(string symbol)
            {
                // TODO: For now this is the only way I know of to tell if a symbol is a unicode A/W variant
                //       I check that there are the A/W variants, and that the base symbol is not already defined.
                if (symbol.EndsWith("A", StringComparison.Ordinal))
                {
                    if (this.ansiMap.TryGetValue(symbol, out UnicodeAlias? alias))
                    {
                        Enforce.Invariant(alias.HaveAnsi == false, "codebug");
                        alias.HaveAnsi = true;
                    }
                    else
                    {
                        string common = symbol.Remove(symbol.Length - 1);
                        alias = new UnicodeAlias(alias: common, ansi: symbol, wide: null, haveAnsi: true, haveWide: false);
                        Enforce.Invariant(!this.wideMap.ContainsKey(alias.Wide), "codebug");
                        this.wideMap.Add(alias.Wide, alias);
                        this.Candidates.Add(alias);
                    }
                }
                else if (symbol.EndsWith("W", StringComparison.Ordinal))
                {
                    if (this.wideMap.TryGetValue(symbol, out UnicodeAlias? alias))
                    {
                        Enforce.Invariant(alias.HaveWide == false, "codebug");
                        alias.HaveWide = true;
                    }
                    else
                    {
                        string common = symbol.Remove(symbol.Length - 1);
                        alias = new UnicodeAlias(alias: common, ansi: null, wide: symbol, haveAnsi: false, haveWide: true);
                        Enforce.Invariant(!this.ansiMap.ContainsKey(alias.Ansi), "codebug");
                        this.ansiMap.Add(alias.Ansi, alias);
                        this.Candidates.Add(alias);
                    }
                }
                else
                {
                    this.NonCandidates.Add(symbol);
                }
            }
        }
    }
}
