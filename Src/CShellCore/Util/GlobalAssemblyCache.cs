﻿#region License
// CShell, A Simple C# Scripting IDE
// Copyright (C) 2013  Arnova Asset Management Ltd., Lukas Buhler
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

// This file is based on code from the SharpDevelop project:
//   Copyright (c) AlphaSierraPapa for the SharpDevelop Team (for details please see \Doc\sharpdevelop-copyright.txt)
//   This code is distributed under the GNU LGPL (for details please see \Doc\COPYING.LESSER.txt)
#endregion
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CShell.Util
{
    /// <summary>
    /// Class with static members to access the content of the global assembly
    /// cache.
    /// </summary>
    internal static class GlobalAssemblyCache
    {
        static readonly string cachedGacPathV2 = Fusion.GetGacPath(false);
        static readonly string cachedGacPathV4 = Fusion.GetGacPath(true);

        public static string GacRootPathV2
        {
            get { return cachedGacPathV2; }
        }

        public static string GacRootPathV4
        {
            get { return cachedGacPathV4; }
        }

        //public static bool IsWithinGac(string assemblyLocation)
        //{
        //    return Core.FileUtility.IsBaseDirectory(GacRootPathV2, assemblyLocation)
        //        || Core.FileUtility.IsBaseDirectory(GacRootPathV4, assemblyLocation);
        //}

        public static List<AssemblyName> GetAssemblyList()
        {
            IApplicationContext applicationContext = null;
            IAssemblyEnum assemblyEnum = null;
            IAssemblyName assemblyName = null;

            List<AssemblyName> l = new List<AssemblyName>();
            Fusion.CreateAssemblyEnum(out assemblyEnum, null, null, 2, 0);
            while (assemblyEnum.GetNextAssembly(out applicationContext, out assemblyName, 0) == 0)
            {
                uint nChars = 0;
                assemblyName.GetDisplayName(null, ref nChars, 0);

                StringBuilder sb = new StringBuilder((int)nChars);
                assemblyName.GetDisplayName(sb, ref nChars, 0);

                l.Add(new AssemblyName(sb.ToString()));
            }
            return l;
        }

        /// <summary>
        /// Gets the full display name of the GAC assembly of the specified short name
        /// </summary>
        public static AssemblyName FindBestMatchingAssemblyName(string name)
        {
            return FindBestMatchingAssemblyName(new AssemblyName(name));
        }

        public static AssemblyName FindBestMatchingAssemblyName(AssemblyName name)
        {
            string[] info;
            Version requiredVersion = name.Version;
            string publicKey = PublicKeyTokenToString(name);

            IApplicationContext applicationContext = null;
            IAssemblyEnum assemblyEnum = null;
            IAssemblyName assemblyName;
            Fusion.CreateAssemblyNameObject(out assemblyName, name.Name, 0, 0);
            Fusion.CreateAssemblyEnum(out assemblyEnum, null, assemblyName, 2, 0);
            List<string> names = new List<string>();

            while (assemblyEnum.GetNextAssembly(out applicationContext, out assemblyName, 0) == 0)
            {
                uint nChars = 0;
                assemblyName.GetDisplayName(null, ref nChars, 0);

                StringBuilder sb = new StringBuilder((int)nChars);
                assemblyName.GetDisplayName(sb, ref nChars, 0);

                string fullName = sb.ToString();
                if (publicKey != null)
                {
                    info = fullName.Split(',');
                    if (publicKey != info[3].Substring(info[3].LastIndexOf('=') + 1))
                    {
                        // Assembly has wrong public key
                        continue;
                    }
                }
                names.Add(fullName);
            }
            if (names.Count == 0)
                return null;
            string best = null;
            Version bestVersion = null;
            Version currentVersion;
            if (requiredVersion != null)
            {
                // use assembly with lowest version higher or equal to required version
                for (int i = 0; i < names.Count; i++)
                {
                    info = names[i].Split(',');
                    currentVersion = new Version(info[1].Substring(info[1].LastIndexOf('=') + 1));
                    if (currentVersion.CompareTo(requiredVersion) < 0)
                        continue; // version not good enough
                    if (best == null || currentVersion.CompareTo(bestVersion) < 0)
                    {
                        bestVersion = currentVersion;
                        best = names[i];
                    }
                }
                if (best != null)
                    return new AssemblyName(best);
            }
            // use assembly with highest version
            best = names[0];
            info = names[0].Split(',');
            bestVersion = new Version(info[1].Substring(info[1].LastIndexOf('=') + 1));
            for (int i = 1; i < names.Count; i++)
            {
                info = names[i].Split(',');
                currentVersion = new Version(info[1].Substring(info[1].LastIndexOf('=') + 1));
                if (currentVersion.CompareTo(bestVersion) > 0)
                {
                    bestVersion = currentVersion;
                    best = names[i];
                }
            }
            return new AssemblyName(best);
        }

        #region FindAssemblyInGac
        // This region is based on code from Mono.Cecil:

        // Author:
        //   Jb Evain (jbevain@gmail.com)
        //
        // Copyright (c) 2008 - 2010 Jb Evain
        //
        // Permission is hereby granted, free of charge, to any person obtaining
        // a copy of this software and associated documentation files (the
        // "Software"), to deal in the Software without restriction, including
        // without limitation the rights to use, copy, modify, merge, publish,
        // distribute, sublicense, and/or sell copies of the Software, and to
        // permit persons to whom the Software is furnished to do so, subject to
        // the following conditions:
        //
        // The above copyright notice and this permission notice shall be
        // included in all copies or substantial portions of the Software.
        //
        // THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
        // EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
        // MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
        // NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
        // LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
        // OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
        // WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        //

        static readonly string[] gac_paths = { GacRootPathV2, GacRootPathV4 };
        static readonly string[] gacs = { "GAC_MSIL", "GAC_32", "GAC" };
        static readonly string[] prefixes = { string.Empty, "v4.0_" };

        /// <summary>
        /// Gets the file name for an assembly stored in the GAC.
        /// </summary>
        public static string FindAssemblyInNetGac(AssemblyName reference)
        {
            // without public key, it can't be in the GAC
            if (reference.GetPublicKeyToken() == null || reference.GetPublicKeyToken().Length == 0)
                return null;

            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < gacs.Length; j++)
                {
                    var gac = Path.Combine(gac_paths[i], gacs[j]);
                    var file = GetAssemblyFile(reference, prefixes[i], gac);
                    if (File.Exists(file))
                        return file;
                }
            }

            return null;
        }

        static string GetAssemblyFile(AssemblyName reference, string prefix, string gac)
        {
            var gac_folder = new StringBuilder()
                .Append(prefix)
                .Append(reference.Version)
                .Append("__");

            gac_folder.Append(PublicKeyTokenToString(reference));

            return Path.Combine(
                Path.Combine(
                    Path.Combine(gac, reference.Name), gac_folder.ToString()),
                reference.Name + ".dll");
        }

        //example from here: http://msdn.microsoft.com/en-us/library/system.reflection.assemblyname.getpublickeytoken(v=vs.95).aspx
        private const byte mask = 15;
        private const string hex = "0123456789ABCDEF";

        public static string PublicKeyTokenToString(AssemblyName assemblyName)
        {
            var pkt = new System.Text.StringBuilder();
            if (assemblyName.GetPublicKeyToken() == null)
                return String.Empty;

            foreach (byte b in assemblyName.GetPublicKeyToken())
            {
                pkt.Append(hex[b / 16 & mask]);
                pkt.Append(hex[b & mask]);
            }
            return pkt.ToString();
        }
        #endregion
    }
}
