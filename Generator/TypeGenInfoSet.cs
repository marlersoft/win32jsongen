// <copyright file="TypeGenInfoSet.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;

    // Note: keeps insertion order (the reason is for predictable code generation)
    internal class TypeGenInfoSet : IEnumerable<TypeGenInfo>
    {
        private readonly List<TypeGenInfo> orderedList;
        private readonly Dictionary<string, TypeGenInfo> fqnMap;

        internal TypeGenInfoSet()
        {
            this.orderedList = new List<TypeGenInfo>();
            this.fqnMap = new Dictionary<string, TypeGenInfo>();
        }

        internal TypeGenInfo this[string fqn]
        {
            get => this.fqnMap[fqn];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new InvalidOperationException();

        public IEnumerator<TypeGenInfo> GetEnumerator() => this.orderedList.GetEnumerator();

        internal void Add(TypeGenInfo info)
        {
            this.orderedList.Add(info);
            this.fqnMap.Add(info.Fqn, info);
        }

        internal bool AddOrVerifyEqual(TypeGenInfo info)
        {
            if (this.fqnMap.TryGetValue(info.Fqn, out TypeGenInfo? other))
            {
                Enforce.Data(object.ReferenceEquals(info, other), Fmt.In(
                    $"found 2 types with the same fully-qualified-name '{info.Fqn}' that are not equal"));
                return false; // already added
            }

            this.Add(info);
            return true; // newly added
        }

        internal bool Contains(TypeGenInfo info) => this.fqnMap.ContainsKey(info.Fqn);
    }
}
