// <copyright file="TypeGenInfoSet.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    // Note: keeps insertion order (the reason is for predictable code generation)
    internal class TypeGenInfoSet : IEnumerable<TypeGenInfo>
    {
        private readonly List<TypeGenInfo> orderedList;
        private readonly Dictionary<string, TypeRefInfo> fqnRefInfoMap;
        private readonly Dictionary<string, OneOrMore<TypeGenInfo>> fqnTypeListMap;

        internal TypeGenInfoSet()
        {
            this.orderedList = new List<TypeGenInfo>();
            this.fqnRefInfoMap = new Dictionary<string, TypeRefInfo>();
            this.fqnTypeListMap = new Dictionary<string, OneOrMore<TypeGenInfo>>();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new InvalidOperationException();

        public IEnumerator<TypeGenInfo> GetEnumerator() => this.orderedList.GetEnumerator();

        internal TypeRefInfo LookupRefInfoByFqn(string fqn) => this.fqnRefInfoMap[fqn];

        internal OneOrMore<TypeGenInfo> LookupTypeInfosByFqn(string fqn) => this.fqnTypeListMap[fqn];

        internal bool Any() { return orderedList.Any(); }

        internal void Add(TypeGenInfo info)
        {
            this.orderedList.Add(info);

            if (this.fqnRefInfoMap.TryGetValue(info.Fqn, out TypeRefInfo? existing))
            {
                Enforce.Data(info.RefInfo.Equals(existing));
            }
            else
            {
                this.fqnRefInfoMap.Add(info.Fqn, info.RefInfo);
            }

            if (this.fqnTypeListMap.TryGetValue(info.Fqn, out OneOrMore<TypeGenInfo>? existingList))
            {
                existingList.Add(info);
            }
            else
            {
                this.fqnTypeListMap.Add(info.Fqn, new OneOrMore<TypeGenInfo>(info));
            }
        }
    }
}
