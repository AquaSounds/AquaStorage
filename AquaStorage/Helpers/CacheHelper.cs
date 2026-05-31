using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using AquaStorage.Models;

namespace AquaStorage.Helpers;

public static class CacheHelper
{
    private static long? _maxCacheBytes = 512 * 1024L; // Default 512KB

    public static void SetMaxCacheBytes(long? maxBytes)
    {
        _maxCacheBytes = maxBytes;
    }

    private static string GetCacheDir()
    {
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(roaming, "AquaStorage", "Cache");
    }

    private static string GetWaveformsDir() => Path.Combine(GetCacheDir(), "Waveforms");
    private static string GetBackgroundsDir() => Path.Combine(GetCacheDir(), "Backgrounds");

    private static void EnsureDir(string dir)
    {
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    // ── Folder Tree ────────────────────────────────────────────────────

    private static string FolderTreePath => Path.Combine(GetCacheDir(), "FolderTree");

    public static void SaveFolderTree(List<TreeNodeData> roots, List<string> rootPaths)
    {
        var dir = GetCacheDir();
        EnsureDir(dir);

        var cache = new FolderTreeCache
        {
            RootPaths = new List<string>(rootPaths),
            Roots = roots
        };

        string json = JsonHelper.Serialize(cache);
        File.WriteAllText(FolderTreePath, json, Encoding.UTF8);
    }

    public static List<TreeNodeData>? LoadFolderTree(List<string> currentPaths)
    {
        try
        {
            if (!File.Exists(FolderTreePath)) return null;

            string json = File.ReadAllText(FolderTreePath, Encoding.UTF8);
            var cache = JsonHelper.Deserialize<FolderTreeCache>(json);
            if (cache == null || cache.Roots.Count == 0) return null;

            if (cache.RootPaths.Count != currentPaths.Count) return null;

            var saved = new HashSet<string>(cache.RootPaths, StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(currentPaths, StringComparer.OrdinalIgnoreCase);
            if (!saved.SetEquals(current)) return null;

            return cache.Roots;
        }
        catch
        {
            return null;
        }
    }

    // ── Waveform ───────────────────────────────────────────────────────

    private static string GetWaveformCachePath(string filePath)
    {
        string hash = ComputePathHash(filePath);
        string dir = GetWaveformsDir();
        EnsureDir(dir);
        return Path.Combine(dir, hash + ".bin");
    }

    public static void SaveWaveform(string filePath, float[] left, float[] right)
    {
        try
        {
            string path = GetWaveformCachePath(filePath);
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(fs);

            bw.Write(left.Length);
            foreach (float v in left) bw.Write(v);
            foreach (float v in right) bw.Write(v);
        }
        catch { }

        EnforceMaxCache();
    }

    public static (float[] left, float[] right)? LoadWaveform(string filePath)
    {
        try
        {
            string path = GetWaveformCachePath(filePath);
            if (!File.Exists(path)) return null;

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            int count = br.ReadInt32();
            if (count <= 0 || count > 100000) return null;

            float[] left = new float[count];
            float[] right = new float[count];

            for (int i = 0; i < count; i++) left[i] = br.ReadSingle();
            for (int i = 0; i < count; i++) right[i] = br.ReadSingle();

            return (left, right);
        }
        catch
        {
            return null;
        }
    }

    // ── Background Image ───────────────────────────────────────────────

    public static string? SaveBackgroundImage(string sourcePath)
    {
        try
        {
            var dir = GetBackgroundsDir();
            EnsureDir(dir);
            string ext = Path.GetExtension(sourcePath);
            string name = $"{Guid.NewGuid():N}{ext}";
            string dest = Path.Combine(dir, name);
            File.Copy(sourcePath, dest, overwrite: true);
            return name;
        }
        catch
        {
            return null;
        }
    }

    public static string? ResolveBackgroundImage(string? originalPath, string? cacheName)
    {
        // 1. Try cache first
        if (!string.IsNullOrEmpty(cacheName))
        {
            string cachePath = Path.Combine(GetBackgroundsDir(), cacheName);
            if (File.Exists(cachePath))
                return cachePath;
        }
        // 2. Fall back to original path
        if (!string.IsNullOrEmpty(originalPath) && File.Exists(originalPath))
            return originalPath;
        // 3. Nothing works
        return null;
    }

    // ── Clear ──────────────────────────────────────────────────────────

    public static void ClearAll()
    {
        try
        {
            string dir = GetCacheDir();
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    public static void ClearWaveformCache()
    {
        try
        {
            string dir = GetWaveformsDir();
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }
    }

    public static void EnforceMaxCache()
    {
        if (_maxCacheBytes is null or <= 0) return;

        try
        {
            string dir = GetWaveformsDir();
            if (!Directory.Exists(dir)) return;

            long maxBytes = _maxCacheBytes.Value;
            var files = new DirectoryInfo(dir).GetFiles("*.bin")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            long total = files.Sum(f => f.Length);
            if (total <= maxBytes) return;

            // Delete oldest files first (reverse order) until under limit
            for (int i = files.Count - 1; i >= 0 && total > maxBytes; i--)
            {
                total -= files[i].Length;
                files[i].Delete();
            }
        }
        catch { }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string ComputePathHash(string filePath)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..16];
    }
}
