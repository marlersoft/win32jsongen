// <copyright file="DecodedTypeAttributes.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System.Globalization;
    using System.IO;
    using System.Reflection;

    internal enum TypeVisibility
    {
        NotPublic,
        Public,
        NestedPublic,
        NestedPrivate,
        NestedFamilyAndAssembly,
        NestedAssembly,
        NestedFamily,
        NestedFamilyOrAssembly,
    }

    internal enum TypeLayoutKind
    {
        Auto,
        Sequential,
        Explicit,
    }

    internal enum TypeClassSemantics
    {
        Class,
        Interface,
    }

    internal struct DecodedTypeAttributes
    {
        internal readonly TypeVisibility Visibility;
        internal readonly TypeLayoutKind Layout;
        internal readonly bool IsInterface;
        internal readonly bool IsAbstract;
        internal readonly bool IsSealed;

        internal DecodedTypeAttributes(TypeAttributes attrs)
        {
            {
                TypeAttributes attrVal = attrs & TypeAttributes.VisibilityMask;
                this.Visibility = attrVal switch
                {
                    TypeAttributes.NotPublic => TypeVisibility.NotPublic,
                    TypeAttributes.Public => TypeVisibility.Public,
                    TypeAttributes.NestedPublic => TypeVisibility.NestedPublic,
                    TypeAttributes.NestedPrivate => TypeVisibility.NestedPrivate,
                    TypeAttributes.NestedFamANDAssem => TypeVisibility.NestedFamilyAndAssembly,
                    TypeAttributes.NestedAssembly => TypeVisibility.NestedAssembly,
                    TypeAttributes.NestedFamily => TypeVisibility.NestedFamily,
                    TypeAttributes.NestedFamORAssem => TypeVisibility.NestedFamilyOrAssembly,
                    _ => throw new InvalidDataException(Fmt.In($"unknown TypeAttribute visibility: {attrVal}")),
                };
            }

            {
                TypeAttributes attrVal = attrs & TypeAttributes.LayoutMask;
                this.Layout = attrVal switch
                {
                    TypeAttributes.AutoLayout => TypeLayoutKind.Auto,
                    TypeAttributes.SequentialLayout => TypeLayoutKind.Sequential,
                    TypeAttributes.ExplicitLayout => TypeLayoutKind.Explicit,
                    _ => throw new InvalidDataException(Fmt.In($"unknown TypeAttribute layout {attrVal}")),
                };
            }

            this.IsInterface = (attrs & TypeAttributes.Interface) != 0;
            this.IsAbstract = (attrs & TypeAttributes.Abstract) != 0;
            this.IsSealed = (attrs & TypeAttributes.Sealed) != 0;
        }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Visibility={0} Layout={1}{2}{3} Sealed={4}",
                this.Visibility,
                this.Layout,
                this.IsInterface ? " Interface" : string.Empty,
                this.IsAbstract ? " Abstract" : string.Empty,
                this.IsSealed);
        }
    }
}
