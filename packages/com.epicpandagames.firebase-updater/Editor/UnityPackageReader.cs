#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace EpicPandaGames.UnityTools.FirebaseUpdater
{
    internal sealed class UnityPackageEntry
    {
        public string Key;
        public string Path;
    }

    internal static class UnityPackageReader
    {
        private const int TarBlockSize = 512;

        public static List<UnityPackageEntry> ReadEntries(string packagePath)
        {
            Dictionary<string, string> paths = new Dictionary<string, string>(StringComparer.Ordinal);
            ReadTar(packagePath, delegate(string name, Stream stream, long size)
            {
                if (!name.EndsWith("/pathname", StringComparison.Ordinal))
                    return;

                string key = name.Substring(0, name.Length - "/pathname".Length);
                string path = ReadText(stream, size).Trim('\0', '\r', '\n');
                paths[key] = NormalizeAndValidateAssetPath(path);
            });

            List<UnityPackageEntry> result = new List<UnityPackageEntry>();
            foreach (KeyValuePair<string, string> pair in paths)
                result.Add(new UnityPackageEntry { Key = pair.Key, Path = pair.Value });
            result.Sort(delegate(UnityPackageEntry left, UnityPackageEntry right)
            {
                return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        public static string ReadAssetText(string packagePath, UnityPackageEntry entry)
        {
            string result = null;
            string wanted = entry.Key + "/asset";
            ReadTar(packagePath, delegate(string name, Stream stream, long size)
            {
                if (result == null && string.Equals(name, wanted, StringComparison.Ordinal))
                    result = ReadText(stream, size);
            });
            return result;
        }

        public static void ExtractAssets(string packagePath, string destinationRoot,
            Func<string, bool> includePath, IDictionary<string, string> contentFingerprints)
        {
            List<UnityPackageEntry> entries = ReadEntries(packagePath);
            Dictionary<string, string> pathByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> directoryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnityPackageEntry entry in entries)
            {
                if (includePath(entry.Path))
                    pathByKey[entry.Key] = entry.Path;
            }
            foreach (UnityPackageEntry entry in entries)
            {
                string prefix = entry.Path.TrimEnd('/') + "/";
                if (entries.Exists(candidate => candidate.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    directoryPaths.Add(entry.Path);
            }

            ReadTar(packagePath, delegate(string name, Stream stream, long size)
            {
                int separator = name.IndexOf('/');
                if (separator <= 0)
                    return;
                string key = name.Substring(0, separator);
                string leaf = name.Substring(separator + 1);
                string assetPath;
                if (!pathByKey.TryGetValue(key, out assetPath) || (leaf != "asset" && leaf != "asset.meta"))
                    return;

                string relativePath = leaf == "asset.meta" ? assetPath + ".meta" : assetPath;
                string destination = Path.Combine(destinationRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                byte[] bytes = ReadBytes(stream, size);
                if (leaf == "asset" && directoryPaths.Contains(assetPath))
                {
                    Directory.CreateDirectory(destination);
                    return;
                }
                string fingerprint = Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(bytes));
                string previous;
                if (contentFingerprints.TryGetValue(relativePath, out previous) && previous != fingerprint)
                    throw new InvalidDataException("Selected Firebase packages contain conflicting content for " + relativePath);
                contentFingerprints[relativePath] = fingerprint;
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.WriteAllBytes(destination, bytes);
            });
        }

        private static string NormalizeAndValidateAssetPath(string path)
        {
            string normalized = path.Replace('\\', '/').Trim();
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) ||
                normalized.Contains("../") || normalized.Contains("/..") || Path.IsPathRooted(normalized))
                throw new InvalidDataException("Unsafe path in Unity package: " + path);
            return normalized;
        }

        private static void ReadTar(string packagePath, Action<string, Stream, long> visitor)
        {
            using (FileStream file = File.OpenRead(packagePath))
            using (GZipStream gzip = new GZipStream(file, CompressionMode.Decompress))
            {
                byte[] header = new byte[TarBlockSize];
                while (ReadExactly(gzip, header, 0, header.Length))
                {
                    if (IsEmptyBlock(header))
                        break;
                    string name = ReadNullTerminated(header, 0, 100);
                    string prefix = ReadNullTerminated(header, 345, 155);
                    if (!string.IsNullOrEmpty(prefix))
                        name = prefix + "/" + name;
                    long size = ParseOctal(header, 124, 12);
                    LimitedReadStream entryStream = new LimitedReadStream(gzip, size);
                    visitor(name, entryStream, size);
                    SkipExactly(entryStream, entryStream.Remaining);
                    long padding = (TarBlockSize - (size % TarBlockSize)) % TarBlockSize;
                    SkipExactly(gzip, padding);
                }
            }
        }

        private static string ReadText(Stream stream, long size)
        {
            return Encoding.UTF8.GetString(ReadBytes(stream, size));
        }

        private static byte[] ReadBytes(Stream stream, long size)
        {
            if (size > int.MaxValue)
                throw new InvalidDataException("Unity package entry is too large.");
            byte[] bytes = new byte[(int)size];
            if (!ReadExactly(stream, bytes, 0, bytes.Length))
                throw new EndOfStreamException("Unexpected end of Unity package.");
            return bytes;
        }

        private static void SkipExactly(Stream stream, long count)
        {
            byte[] buffer = new byte[8192];
            while (count > 0)
            {
                int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, count));
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of Unity package.");
                count -= read;
            }
        }

        private static bool ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, offset + total, count - total);
                if (read <= 0)
                    return total == 0 ? false : throw new EndOfStreamException();
                total += read;
            }
            return true;
        }

        private static bool IsEmptyBlock(byte[] block)
        {
            for (int i = 0; i < block.Length; i++)
                if (block[i] != 0)
                    return false;
            return true;
        }

        private static string ReadNullTerminated(byte[] bytes, int offset, int count)
        {
            int length = 0;
            while (length < count && bytes[offset + length] != 0)
                length++;
            return Encoding.UTF8.GetString(bytes, offset, length);
        }

        private static long ParseOctal(byte[] bytes, int offset, int count)
        {
            string value = ReadNullTerminated(bytes, offset, count).Trim();
            return string.IsNullOrEmpty(value) ? 0 : Convert.ToInt64(value, 8);
        }

        private sealed class LimitedReadStream : Stream
        {
            private readonly Stream source;
            public long Remaining { get; private set; }

            public LimitedReadStream(Stream source, long length)
            {
                this.source = source;
                Remaining = length;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Remaining <= 0)
                    return 0;
                int read = source.Read(buffer, offset, (int)Math.Min(count, Remaining));
                Remaining -= read;
                return read;
            }

            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
#endif
