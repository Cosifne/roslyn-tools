// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;

namespace Roslyn.Insertion
{
    internal struct PackageInfo
    {
        private const string PackageNamePrefix = "VS.ExternalAPIs.";
        private const string PackageExtension = ".nupkg";

        public const string RoslynToolsetPackageName = "VS.Tools.Roslyn";

        /// <summary>
        /// Name of the CoreXT package, e.g. VS.ExternalAPI.Roslyn, Microsoft.DiaSymReader
        /// </summary>
        public readonly string PackageName;

        /// <summary>
        /// Library name, e.g. Roslyn, Microsoft.DiaSymReader.
        /// </summary>
        public readonly string LibraryName;

        /// <summary>
        /// Version, e.g. 1.3.0-beta1-20160315-05
        /// </summary>
        public readonly SemanticVersion Version;

        public bool IsRoslyn => LibraryName == "Roslyn";

        public bool IsRoslynToolsetCompiler => PackageName == RoslynToolsetPackageName;

        public PackageInfo(string packageName, string libraryName, SemanticVersion version)
        {
            PackageName = packageName;
            LibraryName = libraryName;
            Version = version;
        }

        public override string ToString() => $"{PackageName}.{Version}";

        public static PackageInfo ParsePackageFileName(string fileName)
        {
            if (!fileName.EndsWith(PackageExtension))
            {
                throw new InvalidDataException($"Invalid package name: '{fileName}'");
            }

            var libraryNameStartIndex = fileName.StartsWith(PackageNamePrefix) ? PackageNamePrefix.Length : 0;

            var parts = fileName.Substring(libraryNameStartIndex, fileName.Length - libraryNameStartIndex - PackageExtension.Length).Split('.');
            var firstNumber = IndexOfNumericPart(parts);
            if (firstNumber == -1)
            {
                throw new InvalidDataException($"Invalid package name: '{fileName}'");
            }

            var libraryName = string.Join(".", parts.Take(firstNumber));
            var packageName = fileName.Substring(0, libraryNameStartIndex) + libraryName;
            var versionStr = string.Join(".", parts.Skip(firstNumber));
            SemanticVersion version;

            try
            {
                version = SemanticVersion.Parse(versionStr);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Invalid version number: '{fileName}'", e);
            }

            return new PackageInfo(packageName, libraryName, version);
        }

        private static int IndexOfNumericPart(string[] parts)
        {
            for (var i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int number))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
