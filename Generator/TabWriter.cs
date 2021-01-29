// <copyright file="TabWriter.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

namespace JsonWin32Generator
{
    using System;
    using System.IO;

    internal class TabWriter
    {
        private readonly StreamWriter writer;
        private uint depth;

        internal TabWriter(StreamWriter writer)
        {
            this.writer = writer;
        }

        internal void Tab()
        {
            this.depth += 1;
        }

        internal void Untab()
        {
            if (this.depth == 0)
            {
                throw new InvalidOperationException();
            }

            this.depth -= 1;
        }

        internal void WriteLine()
        {
            this.WriteTabs();
            this.writer.WriteLine();
        }

        internal void WriteLine(string s)
        {
            this.WriteTabs();
            this.writer.WriteLine(s);
        }

        internal void WriteLine(string fmt, params object[] args)
        {
            this.WriteTabs();
            this.writer.WriteLine(fmt, args);
        }

        private void WriteTabs()
        {
            for (int i = 0; i < this.depth; i++)
            {
                this.writer.Write("\t"); // todo: benchmark to see if it's worth optimizing this
            }
        }
    }
}
