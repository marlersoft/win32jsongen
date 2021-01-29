// <copyright file="Program.cs" company="https://github.com/marlersoft">
// Copyright (c) https://github.com/marlersoft. All rights reserved.
// </copyright>

using System;
using System.IO;

internal class Program
{
    private static int Main()
    {
        string repoDir = JsonWin32Common.FindWin32JsonRepo();
        string apiDir = JsonWin32Common.GetAndVerifyWin32JsonApiDir(repoDir);
        foreach (string jsonFile in Directory.GetFiles(apiDir))
        {
            Console.WriteLine("TODO: analyze '{0}'", jsonFile);
        }

        return 1;
    }
}
