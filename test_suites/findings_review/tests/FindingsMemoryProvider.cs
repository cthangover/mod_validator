namespace Cthangover.Validator.FindingsReview
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using Cthangover.Core.Mods;

    /// <summary>Provider-only controlled inputs; it never exposes a filesystem fallback.</summary>
    public sealed class FindingsMemoryProvider : IModFileProvider
    {
        private readonly Dictionary<string, string> files;

        /// <summary>Retains the controlled inputs so each context can observe an explicit test revision.</summary>
        public FindingsMemoryProvider(string id, Dictionary<string, string> files)
        {
            Mod = id;
            this.files = files;
        }

        /// <summary>The controlled mod identity reported to source and schema diagnostics.</summary>
        public string Mod { get; }

        /// <summary>No filesystem root exists for these provider-only inputs.</summary>
        public string RootPath
        {
            get
            {
                return null;
            }
        }

        /// <summary>Uses canonical mod paths for presence checks.</summary>
        public bool FileExists(string path)
        {
            return files.ContainsKey(ModPath.Normalize(path));
        }

        /// <summary>Reads the current controlled text revision, or null for an absent path.</summary>
        public string ReadFileText(string path)
        {
            return files.TryGetValue(ModPath.Normalize(path), out var text) ? text : null;
        }

        /// <summary>Returns a fresh UTF-8 buffer owned by the caller.</summary>
        public byte[] ReadFileBinary(string path)
        {
            return ReadFileText(path) is string text ? Encoding.UTF8.GetBytes(text) : null;
        }

        /// <summary>Returns a readable stream owned by the caller, without a filesystem path.</summary>
        public Stream OpenStream(string path)
        {
            return ReadFileBinary(path) is byte[] bytes ? new MemoryStream(bytes, false) : null;
        }

        /// <summary>Never permits consumers to bypass this provider through disk access.</summary>
        public string GetFileSystemPath(string path)
        {
            return null;
        }

        /// <summary>Uses the same canonical include arithmetic as folder and archive providers.</summary>
        public string ResolveIncludePath(string includePath, string currentPath)
        {
            return ModPath.ResolveRelative(includePath, currentPath);
        }

        /// <summary>Returns only immediate children, preserving directory markers for recursive consumers.</summary>
        public IEnumerable<string> ListFiles(string directory = "")
        {
            var prefix = ModPath.NormalizeDirectory(directory);
            var entries = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in files.Keys)
            {
                if (!path.StartsWith(prefix, StringComparison.Ordinal))
                    continue;

                var slash = path.IndexOf('/', prefix.Length);
                entries.Add(slash < 0 ? path : path.Substring(0, slash + 1));
            }

            return entries;
        }

        /// <summary>There are no external handles to release.</summary>
        public void Dispose()
        {
        }
    }
}
