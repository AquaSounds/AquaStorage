using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AquaStorage.Helpers;

/// <summary>
/// Managed wrapper around the Rust FsTree native library for search indexing.
/// </summary>
public sealed class FsTreeService : IDisposable
{
    public const uint RootId = uint.MaxValue;

    private FsTreeHandle? _handle;
    private bool _disposed;
    private readonly SemaphoreSlim _treeLock = new(1, 1);

    public bool IsLoaded => _handle != null && !_handle.IsInvalid;

    /// <summary>
    /// Walk directory trees on a background thread. Used for building search index.
    /// </summary>
    public async Task<bool> WalkAsync(List<string> rootPaths, CancellationToken ct = default)
    {
        ReleaseTree();

        byte[] cancelArr = new byte[1];
        var cancelHandle = GCHandle.Alloc(cancelArr, GCHandleType.Pinned);
        try
        {
            using var ctr = ct.Register(() => cancelArr[0] = 1);

            try
            {
                var ptrs = AllocUtf16Array(rootPaths);
                try
                {
                    var count = rootPaths.Count;
                    var handle = await Task.Run(() =>
                    {
                        unsafe
                        {
                            byte* cancelPtr = (byte*)cancelHandle.AddrOfPinnedObject();
                            fixed (nint* rootPtr = ptrs)
                            {
                                return NativeMethods.walk_tree(rootPtr, count, cancelPtr);
                            }
                        }
                    }, ct);

                    if (handle != nint.Zero)
                    {
                        _handle = new FsTreeHandle(handle);
                        return true;
                    }

                    var err = NativeMethods.GetLastError();
                    if (err == "cancelled")
                        return false;
                    throw new InvalidOperationException($"Walk failed: {err ?? "unknown error"}");
                }
                finally
                {
                    FreeUtf16Array(ptrs);
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        finally
        {
            cancelHandle.Free();
        }
    }

    /// <summary>
    /// Get children of a node. Use RootId for root level.
    /// </summary>
    public List<(uint Id, NodeInfo Info)> GetChildren(uint nodeId, bool audioOnly = false)
    {
        if (_handle == null) return new();

        unsafe
        {
            uint dummy = 0;
            int total = NativeMethods.get_children(_handle.DangerousGetHandle(), nodeId, audioOnly ? 1u : 0u, &dummy, 0);
            if (total <= 0) return new();

            var ids = new uint[total];
            fixed (uint* idsPtr = ids)
            {
                NativeMethods.get_children(_handle.DangerousGetHandle(), nodeId, audioOnly ? 1u : 0u, idsPtr, total);
            }

            var result = new List<(uint, NodeInfo)>(total);
            foreach (var id in ids)
            {
                var info = NativeMethods.get_node_info(_handle.DangerousGetHandle(), id);
                result.Add((id, info));
            }
            return result;
        }
    }

    /// <summary>
    /// Get info for a single node.
    /// </summary>
    public NodeInfo GetNodeInfo(uint nodeId)
    {
        if (_handle == null) return default;
        return NativeMethods.get_node_info(_handle.DangerousGetHandle(), nodeId);
    }

    /// <summary>
    /// Total node count in the Rust tree. Returns 0 if tree not loaded.
    /// </summary>
    public int NodeCount => _handle == null ? 0 : NativeMethods.tree_node_count(_handle.DangerousGetHandle());

    /// <summary>
    /// Get the full filesystem path for a node by its Rust index.
    /// </summary>
    public unsafe string GetNodePath(uint nodeId)
    {
        if (_handle == null) return string.Empty;
        var ptr = NativeMethods.get_node_full_path(_handle.DangerousGetHandle(), nodeId);
        if ((nint)ptr == nint.Zero) return string.Empty;
        var len = NativeMethods.get_node_full_path_len();
        return Marshal.PtrToStringUni((nint)ptr, (int)len) ?? string.Empty;
    }

    /// <summary>
    /// Search tree. Returns (matching IDs, ancestor IDs to expand).
    /// Each call allocates its own cancellation flag so concurrent calls are independent.
    /// </summary>
    public unsafe (List<uint> Matches, List<uint> Ancestors) Search(string query, CancellationToken ct = default)
    {
        if (_handle == null) return (new(), new());

        _treeLock.Wait(ct);
        try
        {
            byte[] cancelArr = new byte[1];
            var cancelHandle = GCHandle.Alloc(cancelArr, GCHandleType.Pinned);
            try
            {
                using var ctr = ct.Register(() => cancelArr[0] = 1);
                byte* cancelPtr = (byte*)cancelHandle.AddrOfPinnedObject();

                fixed (char* queryPtr = query)
                {
                    var resultPtr = NativeMethods.search_tree(_handle.DangerousGetHandle(), queryPtr, cancelPtr);
                    if (resultPtr == nint.Zero) return (new(), new());

                    using var resultHandle = new SearchResultHandle(resultPtr);
                    return UnmarshalSearchResult(resultPtr);
                }
            }
            finally
            {
                cancelHandle.Free();
            }
        }
        finally
        {
            _treeLock.Release();
        }
    }

    /// <summary>
    /// Chunked search: scan nodes in [startFrom, startFrom + maxScan).
    /// Each call allocates its own cancellation flag so concurrent calls are independent.
    /// </summary>
    public unsafe (List<uint> Matches, List<uint> Ancestors) SearchChunked(string query, uint startFrom, uint maxScan, CancellationToken ct = default)
    {
        if (_handle == null) return (new(), new());

        _treeLock.Wait(ct);
        try
        {
            byte[] cancelArr = new byte[1];
            var cancelHandle = GCHandle.Alloc(cancelArr, GCHandleType.Pinned);
            try
            {
                using var ctr = ct.Register(() => cancelArr[0] = 1);
                byte* cancelPtr = (byte*)cancelHandle.AddrOfPinnedObject();

                fixed (char* queryPtr = query)
                {
                    var resultPtr = NativeMethods.search_tree_chunked(_handle.DangerousGetHandle(), queryPtr, startFrom, maxScan, cancelPtr);
                    if (resultPtr == nint.Zero) return (new(), new());

                    using var resultHandle = new SearchResultHandle(resultPtr);
                    return UnmarshalSearchResult(resultPtr);
                }
            }
            finally
            {
                cancelHandle.Free();
            }
        }
        finally
        {
            _treeLock.Release();
        }
    }

    private static unsafe (List<uint> Matches, List<uint> Ancestors) UnmarshalSearchResult(nint resultPtr)
    {
        var result = Marshal.PtrToStructure<SearchResultNative>(resultPtr);
        var matches = new List<uint>((int)result.MatchCount);
        var ancestors = new List<uint>((int)result.AncestorCount);

        for (int i = 0; i < result.MatchCount; i++)
            matches.Add(((uint*)result.MatchIds)[i]);
        for (int i = 0; i < result.AncestorCount; i++)
            ancestors.Add(((uint*)result.AncestorIds)[i]);

        return (matches, ancestors);
    }

    private void ReleaseTree()
    {
        _handle?.Dispose();
        _handle = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseTree();
    }

    // ─── UTF-16 marshaling helpers ─────────────────────────────────────

    private static nint[] AllocUtf16Array(List<string> strings)
    {
        var ptrs = new nint[strings.Count];
        for (int i = 0; i < strings.Count; i++)
            ptrs[i] = Marshal.StringToHGlobalUni(strings[i]);
        return ptrs;
    }

    private static void FreeUtf16Array(nint[] ptrs)
    {
        foreach (var p in ptrs)
            Marshal.FreeHGlobal(p);
    }
}
