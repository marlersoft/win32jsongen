// <copyright file="OneOrMore.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System.Collections.Generic;
    using System.Linq;

    internal class OneOrMore<T>
    {
        private List<T>? others;

        internal OneOrMore(T first)
        {
            this.First = first;
        }

        internal T First { get; }

        internal int Count { get => 1 + ((this.others == null) ? 0 : this.others.Count); }

        internal IEnumerable<T> Others { get => this.others ?? Enumerable.Empty<T>(); }

        internal void Add(T newVal)
        {
            if (this.others == null)
            {
                this.others = new List<T>();
            }

            this.others.Add(newVal);
        }
    }
}
