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
    using System.Runtime.InteropServices;
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

                TypeGenInfo typeInfo = TypeGenInfo.CreateNotNested(typeDef, typeName, typeNamespace, apiNamespaceToName);
                this.typeMap.Add(typeDefHandle, typeInfo);

                Api? api;
                if (!this.apiNamespaceMap.TryGetValue(typeInfo.ApiNamespace, out api))
                {
                    api = new Api(typeInfo.ApiNamespace);
                    this.apiNamespaceMap.Add(typeInfo.ApiNamespace, api);
                }

                // The "Apis" type is a specially-named type reserved to contain all the constant
                // and function declarations for an api.
                if (typeInfo.Name == "Apis")
                {
                    Enforce.Data(api.Constants == null, "multiple Apis types in the same namespace");
                    api.Constants = typeInfo.Def.GetFields();
                    api.Funcs = typeInfo.Def.GetMethods();
                }
                else
                {
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

        internal static void Generate(MetadataReader mr, string outDir)
        {
            JsonGenerator generator = new JsonGenerator(mr);

            foreach (Api api in generator.apiNamespaceMap.Values)
            {
                string filepath = Path.Combine(outDir, api.BaseFileName);
                using var fileStream = new FileStream(filepath, FileMode.Create, FileAccess.Write, FileShare.Read);
                using var streamWriter = new StreamWriter(fileStream, Encoding.UTF8);
                var writer = new TabWriter(streamWriter);
                Console.WriteLine("Api: {0}", api.Name);
                generator.GenerateApi(writer, api);
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

        private void GenerateApi(TabWriter writer, Api api)
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
                    this.GenerateConst(writer, fieldPrefix, fieldDef);
                    writer.Untab();
                    fieldPrefix = ",";
                }
            }
            writer.WriteLine("]");
            var unicodeSet = new UnicodeAliasSet();
            writer.WriteLine();
            writer.WriteLine(",\"Types\":[");
            {
                string fieldPrefix = string.Empty;
                foreach (TypeGenInfo typeInfo in api.TopLevelTypes)
                {
                    writer.Tab();
                    this.GenerateType(writer, fieldPrefix, typeInfo);
                    writer.Untab();
                    fieldPrefix = ",";
                    unicodeSet.RegisterTopLevelSymbol(typeInfo.Name);
                }
            }
            writer.WriteLine("]");
            writer.WriteLine();
            writer.WriteLine(",\"Functions\":[");
            if (api.Funcs != null)
            {
                string fieldPrefix = string.Empty;
                foreach (MethodDefinitionHandle funcHandle in api.Funcs)
                {
                    writer.Tab();
                    var funcName = this.GenerateFunc(writer, fieldPrefix, funcHandle);
                    writer.Untab();
                    fieldPrefix = ",";
                    unicodeSet.RegisterTopLevelSymbol(funcName);
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

        private void GenerateConst(TabWriter writer, string constFieldPrefix, FieldDefinitionHandle fieldDefHandle)
        {
            writer.WriteLine("{0}{{", constFieldPrefix);
            writer.Tab();
            using var defer = Defer.Do(() =>
            {
                writer.Untab();
                writer.WriteLine("}");
            });

            FieldDefinition fieldDef = this.mr.GetFieldDefinition(fieldDefHandle);

            FieldAttributes expected = FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault;
            if (fieldDef.Attributes != expected)
            {
                throw new InvalidOperationException(Fmt.In(
                    $"Expected Constant FieldDefinition to have these attributes '{expected}' but got '{fieldDef.Attributes}'"));
            }
            Enforce.Data(fieldDef.GetOffset() == -1);
            Enforce.Data(fieldDef.GetRelativeVirtualAddress() == 0);

            List<string> jsonAttributes = new List<string>();

            // TODO: what is fieldDef.GetMarshallingDescriptor?
            foreach (CustomAttributeHandle attrHandle in fieldDef.GetCustomAttributes())
            {
                CustomAttr attr = this.DecodeCustomAttr(attrHandle);
                if (object.ReferenceEquals(attr, CustomAttr.Const.Instance))
                {
                    // we already assume "const" on all constant values where this matters (i.e. string literals)
                }
                else if (attr is CustomAttr.NativeTypeInfo nativeTypeInfo)
                {
                    // we already assume null-termination on all constant string literals
                    Enforce.Data(nativeTypeInfo.UnmanagedType == UnmanagedType.LPWStr);
                    Enforce.Data(nativeTypeInfo.IsNullTerminated);
                }
                else if (attr is CustomAttr.Obsolete obsolete)
                {
                    jsonAttributes.Add(Fmt.In($"{{\"Kind\":\"Obsolete\",\"Message\":\"{obsolete.Message}\"}}"));
                }
                else
                {
                    Enforce.Data(false);
                }
            }

            string name = this.mr.GetString(fieldDef.Name);
            Constant constant = this.mr.GetConstant(fieldDef.GetDefaultValue());
            string value = constant.ReadConstValue(this.mr);
            writer.WriteLine("\"Name\":\"{0}\"", name);
            writer.WriteLine(",\"NativeType\":\"{0}\"", constant.TypeCode.ToPrimitiveTypeCode());
            writer.WriteLine(",\"Value\":{0}", value);
            WriteJsonArray(writer, ",\"Attrs\":", jsonAttributes, string.Empty);
        }

        private void GenerateType(TabWriter writer, string typeFieldPrefix, TypeGenInfo typeInfo)
        {
            writer.WriteLine("{0}{{", typeFieldPrefix);
            writer.Tab();
            using var defer = Defer.Do(() =>
            {
                writer.Untab();
                writer.WriteLine("}");
            });
            writer.WriteLine("\"Name\":\"{0}\"", typeInfo.Name);
            Enforce.Data(typeInfo.Def.GetDeclarativeSecurityAttributes().Count == 0);
            Enforce.Data(typeInfo.Def.GetEvents().Count == 0);
            Enforce.Data(typeInfo.Def.GetGenericParameters().Count == 0);
            Enforce.Data(typeInfo.Def.GetMethodImplementations().Count == 0);
            Enforce.Data(typeInfo.Def.GetProperties().Count == 0);
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

            List<string> jsonAttributes = new List<string>();
            bool isNativeTypedef = false;

            foreach (CustomAttributeHandle attrHandle in typeInfo.Def.GetCustomAttributes())
            {
                CustomAttr attr = this.DecodeCustomAttr(attrHandle);
                if (attr is CustomAttr.Guid guidAttr)
                {
                    jsonAttributes.Add(Fmt.In($"{{\"Kind\":\"Guid\",\"Value\":\"{guidAttr.Value}\"}}"));
                }
                else if (attr is CustomAttr.RaiiFree raiiAttr)
                {
                    jsonAttributes.Add(Fmt.In($"{{\"Kind\":\"RAIIFree\",\"FreeFunc\":\"{raiiAttr.FreeFunc}\"}}"));
                }
                else if (attr is CustomAttr.NativeTypedef)
                {
                    isNativeTypedef = true;
                }
                else if (attr is CustomAttr.UnmanagedFunctionPointer)
                {
                    // TODO: do something with this
                }
                else
                {
                    Enforce.Data(false);
                }
            }

            if (isNativeTypedef)
            {
                writer.WriteLine(",\"Kind\":\"NativeTypedef\"");
                Enforce.Data(attrs.Layout == TypeLayoutKind.Sequential);
                Enforce.Data(typeInfo.Def.GetFields().Count == 1);
                FieldDefinition targetDef = this.mr.GetFieldDefinition(typeInfo.Def.GetFields().First());
                string targetDefJson = targetDef.DecodeSignature(this.typeRefDecoder, null).ToJson();
                writer.WriteLine(",\"Def\":{0}", targetDefJson);
                Enforce.Data(typeInfo.Def.GetMethods().Count == 0);
                Enforce.Data(typeInfo.NestedTypeCount == 0);
                return;
            }

            if (typeInfo.Def.BaseType.IsNil)
            {
                writer.WriteLine(",\"Kind\":\"COM\"");

                this.GenerateComType(writer, typeInfo);
                return;
            }

            Enforce.Data(typeInfo.Def.GetInterfaceImplementations().Count == 0);
            Enforce.Data(typeInfo.Def.BaseType.Kind == HandleKind.TypeReference);
            TypeReference baseTypeRef = this.mr.GetTypeReference((TypeReferenceHandle)typeInfo.Def.BaseType);
            NamespaceAndName baseTypeNames = new NamespaceAndName(this.mr.GetString(baseTypeRef.Namespace), this.mr.GetString(baseTypeRef.Name));
            if (baseTypeNames == new NamespaceAndName("System", "Enum"))
            {
                Enforce.Data(attrs.Layout == TypeLayoutKind.Auto);
                this.GenerateEnum(writer, typeInfo);
            }
            else if (baseTypeNames == new NamespaceAndName("System", "ValueType"))
            {
                this.GenerateStruct(writer, typeInfo, attrs.Layout);
            }
            else if (baseTypeNames == new NamespaceAndName("System", "MulticastDelegate"))
            {
                Enforce.Data(attrs.Layout == TypeLayoutKind.Auto);
                this.GenerateFunctionPointer(writer, typeInfo);
            }
            else
            {
                throw new InvalidOperationException();
            }
        }

        private void GenerateMethods(TabWriter writer, TypeGenInfo typeInfo)
        {
            writer.WriteLine(",\"Methods\":[");
            writer.Tab();
            string methodElementPrefix = string.Empty;
            foreach (MethodDefinitionHandle methodDefHandle in typeInfo.Def.GetMethods())
            {
                MethodDefinition methodDef = this.mr.GetMethodDefinition(methodDefHandle);
                writer.WriteLine("{0}\"TODO: Method '{1}'\"", methodElementPrefix, this.mr.GetString(methodDef.Name));
                methodElementPrefix = ",";
            }
            writer.Untab();
            writer.WriteLine("]");
        }

        private void GenerateNestedTypes(TabWriter writer, TypeGenInfo typeInfo)
        {
            string nestedFieldPrefix = string.Empty;
            writer.WriteLine(",\"NestedTypes\":[");
            foreach (TypeGenInfo nestedType in typeInfo.NestedTypesEnumerable)
            {
                writer.Tab();
                this.GenerateType(writer, nestedFieldPrefix, nestedType);
                writer.Untab();
                nestedFieldPrefix = ",";
            }
            writer.WriteLine("]");
        }

        private void GenerateComType(TabWriter writer, TypeGenInfo typeInfo)
        {
            writer.WriteLine(",\"Kind\":\"Com\"");
            writer.WriteLine(",\"Comment\":\"TODO: generate COM type info\"");
            Enforce.Data(typeInfo.Def.GetFields().Count == 0);
            this.GenerateMethods(writer, typeInfo);
            this.GenerateNestedTypes(writer, typeInfo);
        }

        private void GenerateEnum(TabWriter writer, TypeGenInfo typeInfo)
        {
            writer.WriteLine(",\"Kind\":\"Enum\"");
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

        private void GenerateFunctionPointer(TabWriter writer, TypeGenInfo typeInfo)
        {
            writer.WriteLine(",\"Kind\":\"FunctionPointer\"");
            writer.WriteLine(",\"Comment\":\"TODO: implement function pointer type\"");
            Enforce.Data(typeInfo.Def.GetFields().Count == 0);
            Enforce.Data(typeInfo.NestedTypeCount == 0);
            Enforce.Data(typeInfo.Def.GetMethods().Count == 2);

            // TODO: enforce that the 2 methods are .ctor and Invoke
        }

        private void GenerateStruct(TabWriter writer, TypeGenInfo typeInfo, TypeLayoutKind layoutKind)
        {
            string kind;
            if (layoutKind == TypeLayoutKind.Explicit)
            {
                writer.WriteLine(",\"Comment\":\"TODO: Explicit layout data implemented\"");
                kind = "StructOrUnion";
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
                string fieldName = this.mr.GetString(fieldDef.Name);
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
                    Enforce.Data(fieldDef.GetRelativeVirtualAddress() == 0);
                    Constant constant = this.mr.GetConstant(fieldDef.GetDefaultValue());
                    string value = constant.ReadConstValue(this.mr);
                    constFields.Add(Fmt.In($"{constant.TypeCode} {fieldName} = {value}"));
                }
                else
                {
                    Enforce.Data(fieldDef.Attributes == FieldAttributes.Public);
                    string fieldTypeJson = fieldDef.DecodeSignature(this.typeRefDecoder, null).ToJson();
                    writer.WriteLine("{0}{{\"Name\":\"{1}\",\"Type\":{2}}}", fieldElemPrefix, fieldName, fieldTypeJson);
                    fieldElemPrefix = ",";
                }
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
            this.GenerateNestedTypes(writer, typeInfo);
        }

        private string GenerateFunc(TabWriter writer, string funcFieldPrefix, MethodDefinitionHandle funcHandle)
        {
            writer.WriteLine("{0}{{", funcFieldPrefix);
            writer.Tab();
            using var defer = Defer.Do(() =>
            {
                writer.Untab();
                writer.WriteLine("}");
            });

            MethodDefinition funcDef = this.mr.GetMethodDefinition(funcHandle);
            string funcName = this.mr.GetString(funcDef.Name);
            writer.WriteLine("\"Name\":\"{0}\"", funcName);

            // Looks like right now all the functions have these same attributes
            var decodedAttrs = new DecodedMethodAttributes(funcDef.Attributes);
            Enforce.Data(decodedAttrs.MemberAccess == MemberAccess.Public);
            Enforce.Data(decodedAttrs.IsStatic);
            Enforce.Data(!decodedAttrs.IsFinal);
            Enforce.Data(!decodedAttrs.IsVirtual);
            Enforce.Data(!decodedAttrs.IsAbstract);
            Enforce.Data(decodedAttrs.PInvokeImpl);
            Enforce.Data(decodedAttrs.HideBySig);
            Enforce.Data(!decodedAttrs.NewSlot);
            Enforce.Data(!decodedAttrs.SpecialName);
            Enforce.Data(!decodedAttrs.CheckAccessOnOverride);
            Enforce.Data(funcDef.GetCustomAttributes().Count == 0);
            Enforce.Data(funcDef.GetDeclarativeSecurityAttributes().Count == 0);
            Enforce.Data(funcDef.ImplAttributes == MethodImplAttributes.PreserveSig);

            MethodImport methodImport = funcDef.GetImport();
            var methodImportAttrs = new DecodedMethodImportAttributes(methodImport.Attributes);
            Enforce.Data(methodImportAttrs.ExactSpelling);
            Enforce.Data(methodImportAttrs.CharSet == CharSet.None);
            Enforce.Data(methodImportAttrs.BestFit == null);
            Enforce.Data(methodImportAttrs.CallConv == CallConv.Winapi);
            Enforce.Data(methodImportAttrs.ThrowOnUnmapableChar == null);

            Enforce.Data(this.mr.GetString(methodImport.Name) == funcName);

            ModuleReference moduleRef = this.mr.GetModuleReference(methodImport.Module);
            Enforce.Data(moduleRef.GetCustomAttributes().Count == 0);
            string importName = this.mr.GetString(moduleRef.Name);

            MethodSignature<TypeRef> methodSig = funcDef.DecodeSignature(this.typeRefDecoder, null);

            Enforce.Data(methodSig.Header.Kind == SignatureKind.Method);
            Enforce.Data(methodSig.Header.CallingConvention == SignatureCallingConvention.Default);
            Enforce.Data(methodSig.Header.Attributes == SignatureAttributes.None);

            writer.WriteLine(",\"SetLastError\":{0}", methodImportAttrs.SetLastError ? "true" : "false");
            writer.WriteLine(",\"DllImport\":\"{0}\"", importName);
            writer.WriteLine(",\"ReturnType\":{0}", methodSig.ReturnType.ToJson());
            writer.WriteLine(",\"Params\":[");
            writer.Tab();
            string paramFieldPrefix = string.Empty;
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

                // TODO: handle param.GetCustomAttributes()
                // TODO: handle param.GetMarshallingDescriptor();
                Enforce.Data(param.GetDefaultValue().IsNil);
                List<string> jsonAttributes = new List<string>();
                ParameterAttributes remainingAttrs = param.Attributes;
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
                    jsonAttributes.Add("\"Optional\"");
                }
                Enforce.Data(remainingAttrs == ParameterAttributes.None);
                foreach (CustomAttributeHandle attrHandle in param.GetCustomAttributes())
                {
                    // TODO: handle these
                    //CustomAttr attr = this.DecodeCustomAttr(this.mr.GetCustomAttribute(attrHandle));
                }

                string paramName = this.mr.GetString(param.Name);
                Enforce.Data(paramName.Length > 0);

                var paramType = methodSig.ParameterTypes[param.SequenceNumber - 1];
                string prefix = Fmt.In($"{paramFieldPrefix}{{\"Name\":\"{paramName}\",\"Type\":{paramType.ToJson()},\"Attrs\":");
                WriteJsonArray(writer, prefix, jsonAttributes, "}");
                paramFieldPrefix = ",";
            }
            writer.Untab();
            writer.WriteLine("]");
            return funcName;
        }

#pragma warning restore SA1513 // Closing brace should be followed by blank line

        private CustomAttr DecodeCustomAttr(CustomAttributeHandle attrHandle)
        {
            CustomAttribute attr = this.mr.GetCustomAttribute(attrHandle);
            NamespaceAndName attrName = attr.GetAttrTypeName(this.mr);
            CustomAttributeValue<CustomAttrType> attrArgs = attr.DecodeValue(CustomAttrDecoder.Instance);
            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "ConstAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return CustomAttr.Const.Instance;
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "NativeTypeInfoAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 1);
                UnmanagedType unmanagedType = Enforce.AttrFixedArgAsUnmanagedType(attrArgs.FixedArguments[0]);
                Enforce.NamedArgName(attrName, attrArgs, "IsNullTerminated", 0);
                bool isNullTerminated = Enforce.AttrNamedAsBool(attrArgs.NamedArguments[0]);
                return new CustomAttr.NativeTypeInfo(unmanagedType, isNullTerminated);
            }

            if (attrName == new NamespaceAndName("System", "ObsoleteAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.Obsolete(Enforce.AttrFixedArgAsString(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("System.Runtime.InteropServices", "GuidAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.Guid(Enforce.AttrFixedArgAsString(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "RAIIFreeAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.RaiiFree(Enforce.AttrFixedArgAsString(attrArgs.FixedArguments[0]));
            }

            if (attrName == new NamespaceAndName("Windows.Win32.Interop", "NativeTypedefAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 0);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.NativeTypedef();
            }

            if (attrName == new NamespaceAndName("System.Runtime.InteropServices", "UnmanagedFunctionPointerAttribute"))
            {
                Enforce.AttrFixedArgCount(attrName, attrArgs, 1);
                Enforce.AttrNamedArgCount(attrName, attrArgs, 0);
                return new CustomAttr.UnmanagedFunctionPointer();
            }

            throw new NotImplementedException(Fmt.In($"unhandled custom attribute \"{attrName.Namespace}\", \"{attrName.Name}\""));
        }

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
