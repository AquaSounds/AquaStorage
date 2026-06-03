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
    private byte _cancelFlag;
    private bool _disposed;

    public bool IsLoaded => _handle != null && !_handle.IsInvalid;

    /// <summary>
    /// Walk directory trees on a background thread. Used for building search index.
    /// </summary>
    public async Task<bool> WalkAsync(List<string> rootPaths, CancellationToken ct = default)
    {
        ReleaseTree();

        _cancelFlag = 0;
        using var ctr = ct.Register(() => _cancelFlag = 1);

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
                        fixed (byte* cancelPtr = &_cancelFlag)
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
    /// Search tree. Returns (matching IDs, ancestor IDs to expand).
    /// </summary>
    public unsafe (List<uint> Matches, List<uint> Ancestors) Search(string query, CancellationToken ct = default)
    {
        if (_handle == null) return (new(), new());

        _cancelFlag = 0;
        using var ctr = ct.Register(() => _cancelFlag = 1);

        fixed (byte* cancelPtr = &_cancelFlag)
        fixed (char* queryPtr = query)
        {
            var resultPtr = NativeMethods.search_tree(_handle.DangerousGetHandle(), queryPtr, cancelPtr);
            if (resultPtr == nint.Zero) return (new(), new());

            using var resultHandle = new SearchResultHandle(resultPtr);
            unsafe
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
        }
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
