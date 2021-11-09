// <copyright file="Patch.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>
#pragma warning disable SA1649 // File name should match first type name
#pragma warning disable SA1402 // File may only contain a single type
#pragma warning disable SA1201 // Elements should appear in the correct order

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;

    internal abstract class PatchConfig
    {
#pragma warning disable CA1825 // Avoid zero-length array allocations
#pragma warning disable SA1202 // Elements should be ordered by access
        internal static readonly Const[] Consts = new Const[]
        {
            // NOTE: no issue filed for this yet
            new Const(Api: "Devices.Usb", Name: "WinUSB_TestGuid", Duplicated: true),
        };

        private static readonly Param[] OptionalHwndParam = new Param[] { new Param(Name: "hWnd", Optional: true) };

        internal static readonly Func[] Funcs = new Func[]
        {
            new Func(Api: "System.Memory", Name: "CreateFileMappingA", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "System.Memory", Name: "CreateFileMappingW", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "System.Memory", Name: "MapViewOfFile", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "System.Memory", Name: "MapViewOfFileEx", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "UI.WindowsAndMessaging", Name: "ShowWindow", Params: OptionalHwndParam),
            new Func(Api: "UI.WindowsAndMessaging", Name: "CreateWindowExA", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "UI.WindowsAndMessaging", Name: "CreateWindowExW", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "Graphics.Gdi", Name: "CreateFontA", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "Graphics.Gdi", Name: "CreateFontW", ReturnType: new ReturnType(Optional: true)),
            new Func(Api: "System.Console", Name: "WriteConsoleA", Params: new Param[] { new Param(Name: "lpReserved", Optional: true) }),
            new Func(Api: "System.Console", Name: "WriteConsoleW", Params: new Param[] { new Param(Name: "lpReserved", Optional: true) }),
        };

        private static readonly Field[] WNDCLASSFields = new Field[]
        {
            new Field(Name: "hIcon", Optional: true),
            new Field(Name: "hCursor", Optional: true),
            new Field(Name: "hbrBackground", Optional: true),
            new Field(Name: "lpszMenuName", Optional: true),
        };

        internal static readonly Type[] Types = new Type[]
        {
            new Type(Api: "UI.WindowsAndMessaging", Name: "WNDCLASSA", Fields: WNDCLASSFields),
            new Type(Api: "UI.WindowsAndMessaging", Name: "WNDCLASSW", Fields: WNDCLASSFields),

            // Workaround https://github.com/microsoft/win32metadata/issues/737
            new Type(Api: "System.Iis", Name: "CONFIGURATION_ENTRY", Remove: true),
            new Type(Api: "System.Iis", Name: "LOGGING_PARAMETERS", Remove: true),
            new Type(Api: "System.Iis", Name: "PRE_PROCESS_PARAMETERS", Remove: true),
            new Type(Api: "System.Iis", Name: "POST_PROCESS_PARAMETERS", Remove: true),
        };

        internal static readonly ComType[] ComTypes = new ComType[]
        {
            // Workaround https://github.com/microsoft/win32metadata/issues/736
            new ComType(Type: new Type(Api: "NetworkManagement.NetManagement", Name: "INetCfgComponentUpperEdge"), Funcs: new Func[]
            {
                new Func(Api: "NetworkManagement.NetManagement", Name: "AddInterfacesToAdapter", SkipParams: true),
            }),
        };

        // Have to disable this warning here because compiler unable to detect when record fields are used
#pragma warning disable CA1801 // Review unused parameters
        internal record Const(string Api, string Name, bool Duplicated = false);

        internal record Param(string Name, bool Optional = false, bool Const = false);

        internal record ReturnType(bool Optional = false);

        internal record Func(string Api, string Name, bool SkipParams = false, ReturnType? ReturnType = null, Param[]? Params = null);

        internal record Field(string Name, string? Type = null, bool Optional = false);

        internal record Type(string Api, string Name, bool Remove = false, Field[]? Fields = null, Type[]? NestedTypes = null);

        internal record ComType(Type Type, Func[]? Funcs = null);
#pragma warning restore CA1801 // Review unused parameters

        internal static FuncPatch CreateFuncPatch(Func func)
        {
            var paramMap = new Dictionary<string, ParamPatch>();
            if (func.Params != null)
            {
                foreach (Param param in func.Params)
                {
                    paramMap.Add(param.Name, new ParamPatch(param));
                }
            }

            return new FuncPatch(func, paramMap);
        }

        internal static Dictionary<string, ApiPatch> CreateApiMap()
        {
            var apiMap = new Dictionary<string, ApiPatch>();

            foreach (Const c in Consts)
            {
                apiMap.GetOrCreate(c.Api).ConstMap.Add(c.Name, new ConstPatch(c));
            }

            foreach (Type type in Types)
            {
                apiMap.GetOrCreate(type.Api).TypeMap.Add(type.Name, CreateTypePatch(type));
            }

            foreach (ComType type in ComTypes)
            {
                apiMap.GetOrCreate(type.Type.Api).TypeMap.Add(type.Type.Name, CreateComTypePatch(type));
            }

            foreach (Func func in Funcs)
            {
                ApiPatch apiPatch = apiMap.GetOrCreate(func.Api);
                apiPatch.FuncMap.Add(func.Name, CreateFuncPatch(func));
            }

            return apiMap;
        }

        private static TypePatch CreateTypePatch(Type type)
        {
            Dictionary<string, FieldPatch> fieldMap = Patch.EmptyFieldMap;
            if (type.Fields != null)
            {
                fieldMap = new Dictionary<string, FieldPatch>();
                foreach (Field field in type.Fields)
                {
                    fieldMap.Add(field.Name, new FieldPatch(field));
                }
            }

            Dictionary<string, TypePatch> nestedTypeMap = Patch.EmptyTypeMap;
            if (type.NestedTypes != null)
            {
                nestedTypeMap = new Dictionary<string, TypePatch>();
                foreach (Type nestedType in type.NestedTypes)
                {
                    nestedTypeMap.Add(nestedType.Name, CreateTypePatch(nestedType));
                }
            }

            return new TypePatch(type, fieldMap, nestedTypeMap);
        }

        private static TypePatch CreateComTypePatch(ComType type)
        {
            Dictionary<string, FuncPatch> funcMap = Patch.EmptyFuncMap;
            if (type.Funcs != null)
            {
                funcMap = new Dictionary<string, FuncPatch>();
                foreach (Func func in type.Funcs)
                {
                    funcMap.Add(func.Name, CreateFuncPatch(func));
                }
            }

            return new ComTypePatch(type.Type, funcMap);
        }
    }

    internal abstract class Patch
    {
        internal static readonly PatchConfig.Const EmptyConstConfig = new PatchConfig.Const(Api: string.Empty, Name: string.Empty);
        internal static readonly PatchConfig.Type EmptyTypeConfig = new PatchConfig.Type(Api: string.Empty, Name: string.Empty);
        internal static readonly PatchConfig.Func EmptyFuncConfig = new PatchConfig.Func(Api: string.Empty, Name: string.Empty, Params: Array.Empty<PatchConfig.Param>());

        internal static readonly Dictionary<string, ConstPatch> EmptyConstMap = new Dictionary<string, ConstPatch>();
        internal static readonly Dictionary<string, TypePatch> EmptyTypeMap = new Dictionary<string, TypePatch>();
        internal static readonly Dictionary<string, FuncPatch> EmptyFuncMap = new Dictionary<string, FuncPatch>();
        internal static readonly Dictionary<string, FieldPatch> EmptyFieldMap = new Dictionary<string, FieldPatch>();
        internal static readonly Dictionary<string, ParamPatch> EmptyParamMap = new Dictionary<string, ParamPatch>();

        internal static readonly ApiPatch EmptyApi = new ApiPatch(EmptyConstMap, EmptyTypeMap, EmptyFuncMap);
        internal static readonly ConstPatch EmptyConst = new ConstPatch(EmptyConstConfig);
        internal static readonly TypePatch EmptyType = new TypePatch(EmptyTypeConfig, EmptyFieldMap, EmptyTypeMap);
        internal static readonly ComTypePatch EmptyComType = new ComTypePatch(EmptyTypeConfig, EmptyFuncMap);
        internal static readonly FuncPatch EmptyFunc = new FuncPatch(EmptyFuncConfig, EmptyParamMap);

        internal uint ApplyCount { get; set; }
    }

    internal delegate void PatchCallback(Patch patch);

    internal class ApiPatch : Patch, ITypePatchMap, IFuncPatchMap
    {
        public ApiPatch()
            : this(new Dictionary<string, ConstPatch>(), new Dictionary<string, TypePatch>(), new Dictionary<string, FuncPatch>())
        {
        }

        internal ApiPatch(Dictionary<string, ConstPatch> constMap, Dictionary<string, TypePatch> typeMap, Dictionary<string, FuncPatch> funcMap)
        {
            this.ConstMap = constMap;
            this.TypeMap = typeMap;
            this.FuncMap = funcMap;
        }

        public Dictionary<string, ConstPatch> ConstMap { get; }

        public Dictionary<string, TypePatch> TypeMap { get; }

        public Dictionary<string, FuncPatch> FuncMap { get; }

        internal void SelectSubPatches(PatchCallback callback)
        {
            foreach (var patch in this.ConstMap.Values)
            {
                callback(patch);
            }

            foreach (var patch in this.TypeMap.Values)
            {
                callback(patch);
                patch.SelectSubPatches(callback);
            }

            foreach (var patch in this.FuncMap.Values)
            {
                callback(patch);
                patch.SelectSubPatches(callback);
            }
        }
    }

    internal interface ITypePatchMap
    {
        internal static readonly ITypePatchMap None = new Empty();

        public Dictionary<string, TypePatch> TypeMap { get; }

        private class Empty : ITypePatchMap
        {
            public Dictionary<string, TypePatch> TypeMap => Patch.EmptyTypeMap;
        }
    }

    internal interface IFuncPatchMap
    {
        internal static readonly IFuncPatchMap None = new Empty();

        public Dictionary<string, FuncPatch> FuncMap { get; }

        private class Empty : IFuncPatchMap
        {
            public Dictionary<string, FuncPatch> FuncMap => Patch.EmptyFuncMap;
        }
    }

    internal class ConstPatch : Patch
    {
        internal ConstPatch(PatchConfig.Const config)
        {
            this.Config = config;
        }

        internal PatchConfig.Const Config { get; }
    }

    internal class TypePatch : Patch
    {
        internal TypePatch(
            PatchConfig.Type config,
            Dictionary<string, FieldPatch> fieldMap,
            Dictionary<string, TypePatch> nestedTypeMap)
        {
            this.Config = config;
            this.FieldMap = fieldMap;
            this.NestedTypeMap = nestedTypeMap;
        }

        internal PatchConfig.Type Config { get; }

        internal Dictionary<string, FieldPatch> FieldMap { get; }

        internal Dictionary<string, TypePatch> NestedTypeMap { get; }

        internal virtual ComTypePatch ToComPatch()
        {
            if (object.ReferenceEquals(this, Patch.EmptyType))
            {
                return Patch.EmptyComType;
            }

            throw Violation.Data(Fmt.In($"a COM type is being patched as a non-COM type"));
        }

        internal void SelectSubPatches(PatchCallback callback)
        {
            foreach (FieldPatch fieldPatch in this.FieldMap.Values)
            {
                callback(fieldPatch);
            }

            foreach (TypePatch nestedTypePatch in this.NestedTypeMap.Values)
            {
                callback(nestedTypePatch);
                nestedTypePatch.SelectSubPatches(callback);
            }
        }
    }

    internal class ReturnTypePatch : Patch
    {
        public ReturnTypePatch(PatchConfig.ReturnType config)
        {
            this.Config = config;
        }

        public PatchConfig.ReturnType Config { get; }

        public override string ToString()
        {
            return Fmt.In($"ReturnType");
        }
    }

    internal class ParamPatch : Patch
    {
        public ParamPatch(PatchConfig.Param config)
        {
            this.Config = config;
        }

        public PatchConfig.Param Config { get; }

        public override string ToString()
        {
            return Fmt.In($"Parameter '{this.Config.Name}'");
        }
    }

    internal class FieldPatch : Patch
    {
        public FieldPatch(PatchConfig.Field config)
        {
            this.Config = config;
        }

        public PatchConfig.Field Config { get; }

        public override string ToString()
        {
            return Fmt.In($"Field '{this.Config.Name}'");
        }
    }

    internal class FuncPatch : Patch
    {
        public override string ToString()
        {
            return Fmt.In($"Function '{this.Func.Name}'");
        }

        internal FuncPatch(PatchConfig.Func func, Dictionary<string, ParamPatch> paramMap)
        {
            this.Func = func;
            this.ReturnType = (func.ReturnType == null) ? null : new ReturnTypePatch(func.ReturnType);
            this.ParamMap = paramMap;
        }

        internal PatchConfig.Func Func { get; }

        internal ReturnTypePatch? ReturnType { get; }

        internal Dictionary<string, ParamPatch> ParamMap { get; }

        internal void SelectSubPatches(PatchCallback callback)
        {
            if (this.ReturnType != null)
            {
                callback(this.ReturnType);
            }

            foreach (ParamPatch paramPatch in this.ParamMap.Values)
            {
                callback(paramPatch);
            }
        }
    }

    internal class ComTypePatch : TypePatch, IFuncPatchMap
    {
        internal ComTypePatch(PatchConfig.Type config, Dictionary<string, FuncPatch> funcMap)
            : base(config, Patch.EmptyFieldMap, Patch.EmptyTypeMap)
        {
            this.FuncMap = funcMap;
        }

        public Dictionary<string, FuncPatch> FuncMap { get; }

        internal override ComTypePatch ToComPatch() => this;

        internal new void SelectSubPatches(PatchCallback callback)
        {
            base.SelectSubPatches(callback);

            foreach (FuncPatch funcPatch in this.FuncMap.Values)
            {
                callback(funcPatch);
                funcPatch.SelectSubPatches(callback);
            }
        }
    }

    internal class OptionalMap
    {
        internal static TValue Get<TKey, TValue>(
            IDictionary<TKey, TValue>? optionalMap,
            TKey key,
            TValue defaultValue)
        {
            if (optionalMap != null && optionalMap.TryGetValue(key, out TValue? value))
            {
                return value;
            }

            return defaultValue;
        }
    }
}