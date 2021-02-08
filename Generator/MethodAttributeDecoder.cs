// <copyright file="MethodAttributeDecoder.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

#pragma warning disable SA1649 // File name should match first type name

namespace JsonWin32Generator
{
    using System.Globalization;
    using System.IO;
    using System.Reflection;

    internal enum MemberAccess
    {
        PrivateScope,
        Private,
        FamilyAndAssembly,
        Assembly,
        Family,
        FamilyOrAssembly,
        Public,
    }

    internal enum CharSet
    {
        None,
        Ansi,
        Unicode,
        Auto,
    }

    internal enum CallConv
    {
        None,
        Winapi,
        CDecl,
        Stdcall,
        Thiscall,
        Fastcall,
    }

    // MethodAttribute Notes:
    //
    // Masks:
    //     m: MemberAccessMask
    //     v: VtableLayoutMask
    //     r: ReservedMask(runtime use only)
    //
    //     rr-r ---v ---- -mmm
    //
    // Flags:
    //
    //     RequireSecObject
    //     | PinvokeImpl
    //     | |  SpecialName
    //     | |  | CheckAccessOnOverride
    //     | |  | |  HideBySig
    //     | |  | |  | Final
    //     | |  | |  | | UnmanagedExport
    //     | |  | |  | |  |
    //     | |  | |  | |  |
    //     rr-r ---v ---- -mmm
    //      | |  |    | |
    //      | |  |    | |
    //      | |  |    | |
    //      | |  |    | Static
    //      | |  |    Virtual
    //      | |  |
    //      | |  Abstract
    //      | RTSpecialName
    //      HasSecurity
    //
    // The MemberAccessMask Enumeration:
    //
    //     # | Name         | Description
    //     --| -------------| ---------
    //     0 | PrivateScope | inaccessible
    //     1 | Private      | accessible only by this type
    //     2 | FamANDAssem  | accessible by this class and its derived classes but only in this assembly
    //     3 | Assembly     | accessible to any class in this assembly
    //     4 | Family       | accessible only to members of this class and its derived classes
    //     5 | FamORAssem   | accessible to this class and its derived classes AND also any type in the assembly
    //     6 | Public       | accessible to anyone
    //
    // The VtableLayoutMask Enumeration:
    //
    //     NOTE: Since there are only 2 values, I'm not sure why this was made into an enumeration
    //           instead of just a flag like everything else.
    //
    //     #  | Name        | Description
    //     ---| ------------| ---------
    //      0 | ReuseSlot   | method will reuse an existing slot in the vtable (default)
    //     256| NewSlot     | method always gets a new slot in the vtable
    internal readonly struct DecodedMethodAttributes
    {
        public readonly MemberAccess MemberAccess;
        public readonly bool UnmanagedExport;
        public readonly bool IsStatic;
        public readonly bool IsFinal;
        public readonly bool IsVirtual;
        public readonly bool HideBySig;
        public readonly bool NewSlot;
        public readonly bool CheckAccessOnOverride;
        public readonly bool IsAbstract;
        public readonly bool SpecialName;
        public readonly bool PInvokeImpl;

        public DecodedMethodAttributes(MethodAttributes attrs)
        {
            {
                MethodAttributes attrValue = attrs & MethodAttributes.MemberAccessMask;
                this.MemberAccess = attrValue switch
                {
                    MethodAttributes.PrivateScope => MemberAccess.PrivateScope,
                    MethodAttributes.Private => MemberAccess.Private,
                    MethodAttributes.FamANDAssem => MemberAccess.FamilyAndAssembly,
                    MethodAttributes.Assembly => MemberAccess.Assembly,
                    MethodAttributes.Family => MemberAccess.Family,
                    MethodAttributes.FamORAssem => MemberAccess.FamilyOrAssembly,
                    MethodAttributes.Public => MemberAccess.Public,
                    _ => throw new InvalidDataException(Fmt.In($"unknown MethodAttributes member_access {attrValue}")),
                };
            }

            this.UnmanagedExport = (attrs & MethodAttributes.UnmanagedExport) != 0;
            this.IsStatic = (attrs & MethodAttributes.Static) != 0;
            this.IsFinal = (attrs & MethodAttributes.Final) != 0;
            this.IsVirtual = (attrs & MethodAttributes.Virtual) != 0;
            this.HideBySig = (attrs & MethodAttributes.HideBySig) != 0;
            this.NewSlot = (attrs & MethodAttributes.NewSlot) != 0;
            this.CheckAccessOnOverride = (attrs & MethodAttributes.CheckAccessOnOverride) != 0;
            this.IsAbstract = (attrs & MethodAttributes.Abstract) != 0;
            this.SpecialName = (attrs & MethodAttributes.SpecialName) != 0;
            this.PInvokeImpl = (attrs & MethodAttributes.PinvokeImpl) != 0;
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}{1}{2}{3}{4}{5}{6}{7}{8}{9}",
                this.MemberAccess,
                this.IsStatic ? " static" : string.Empty,
                this.IsFinal ? " final" : string.Empty,
                this.IsVirtual ? " virtual" : string.Empty,
                this.IsAbstract ? " abstract" : string.Empty,
                this.PInvokeImpl ? " pinvoke" : string.Empty,
                this.HideBySig ? " HideBySig" : string.Empty,
                this.NewSlot ? " NewSlot" : string.Empty,
                this.CheckAccessOnOverride ? " CheckOverrideAccess" : string.Empty,
                this.SpecialName ? " SpecialName" : string.Empty);
        }
    }

    // MethodImportAttribute Notes:
    //
    // Masks:
    //     b: BestFitMappingMask
    //     c: CallingConventionMask
    //     s: CharSetMask
    //     t: ThrowOnUnmappableCharMask
    //
    //     --tt -ccc --bb -ss-
    //
    // Flags:
    //
    //     --tt -ccc --bb -ss-
    //                |      |
    //                |      ExactSpelling
    //                |
    //                SetLastError
    //
    // BestFitMappingMask Enumeration:
    //
    //     #  | Name                  |
    //     ---| ----------------------|
    //     16 | BestFitMappingEnable  |
    //     32 | BestFitMappingDisable |
    //
    // CallingConventionMask Enumeration:
    //
    //     #    | Name                      |
    //     -----| --------------------------|
    //        0 | None                      |
    //      256 | CallingConventionWinApi   |
    //      512 | CallingConventionCDecl    |
    //      768 | CallingConventionStdCall  |
    //     1024 | CallingConventionThisCall |
    //     1280 | CallingConventionFastCall |
    //
    // CharSetMask Enumeration:
    //
    //     # | Name           |
    //     --| ---------------|
    //     2 | CharSetAnsi    |
    //     4 | CharSetUnicode |
    //     6 | CharSetAuto    |
    //
    // ThrowOnUnmappableCharMask Enumeration:
    //
    //     #    | Name                         |
    //     -----| -----------------------------|
    //     4096 | ThrowOnUnmappableCharEnable  |
    //     8192 | ThrowOnUnmappableCharDisable |
    internal readonly struct DecodedMethodImportAttributes
    {
        public readonly bool ExactSpelling;
        public readonly CharSet CharSet;
        public readonly bool? BestFit;
        public readonly bool SetLastError;
        public readonly CallConv CallConv;
        public readonly bool? ThrowOnUnmapableChar;

        public DecodedMethodImportAttributes(MethodImportAttributes attrs)
        {
            this.ExactSpelling = (attrs & MethodImportAttributes.ExactSpelling) != 0;
            {
                MethodImportAttributes char_set_attr = attrs & MethodImportAttributes.CharSetMask;
                this.CharSet = char_set_attr switch
                {
                    MethodImportAttributes.None => CharSet.None,
                    MethodImportAttributes.CharSetAnsi => CharSet.Ansi,
                    MethodImportAttributes.CharSetUnicode => CharSet.Unicode,
                    MethodImportAttributes.CharSetAuto => CharSet.Auto,
                    _ => throw new InvalidDataException(Fmt.In($"unknown MethodImportAttributes char_set {char_set_attr}")),
                };
            }

            {
                MethodImportAttributes best_fit_attr = attrs & MethodImportAttributes.BestFitMappingMask;
                this.BestFit = best_fit_attr switch
                {
                    MethodImportAttributes.None => null,
                    MethodImportAttributes.BestFitMappingDisable => false,
                    MethodImportAttributes.BestFitMappingEnable => true,
                    _ => throw new InvalidDataException(Fmt.In($"unknown MethodImportAttributes best_fit {best_fit_attr}")),
                };
            }

            this.SetLastError = (attrs & MethodImportAttributes.SetLastError) != 0;
            {
                MethodImportAttributes call_conv_attr = attrs & MethodImportAttributes.CallingConventionMask;
                this.CallConv = call_conv_attr switch
                {
                    MethodImportAttributes.None => CallConv.None,
                    MethodImportAttributes.CallingConventionWinApi => CallConv.Winapi,
                    MethodImportAttributes.CallingConventionCDecl => CallConv.CDecl,
                    MethodImportAttributes.CallingConventionStdCall => CallConv.Stdcall,
                    MethodImportAttributes.CallingConventionThisCall => CallConv.Thiscall,
                    MethodImportAttributes.CallingConventionFastCall => CallConv.Fastcall,
                    _ => throw new InvalidDataException(Fmt.In($"unknown MethodImportAttributes call_conv {call_conv_attr}")),
                };
            }

            {
                MethodImportAttributes throw_attr = attrs & MethodImportAttributes.ThrowOnUnmappableCharMask;
                this.ThrowOnUnmapableChar = throw_attr switch
                {
                    MethodImportAttributes.None => null,
                    MethodImportAttributes.ThrowOnUnmappableCharDisable => false,
                    MethodImportAttributes.ThrowOnUnmappableCharEnable => true,
                    _ => throw new InvalidDataException(Fmt.In($"unknown MethodImportAttributes throw_on_unmappable {throw_attr}")),
                };
            }
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "CharSet={0}{1} SetLastError={2} BestFit={3} CallConv={4}{5}",
                this.CharSet,
                this.ExactSpelling ? " ExactSpelling" : string.Empty,
                this.SetLastError,
                this.BestFit is bool bb ? (bb ? "true" : "false") : "default",
                this.CallConv,
                this.ThrowOnUnmapableChar is bool tb ? (tb ? " ThrowOnUnmappableChar" : string.Empty) : string.Empty);
        }
    }
}
