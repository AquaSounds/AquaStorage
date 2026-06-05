using System;
using System.Runtime.InteropServices;

namespace AquaStorage.Helpers;

[StructLayout(LayoutKind.Sequential)]
public struct NodeInfo
{
    public unsafe fixed ushort Name[256];
    public uint NameLen;
    public byte IsDir;
    public byte IsAudio;
    public ulong SizeBytes;
    public uint ChildCount;
    public byte HasAudioInSubtree;
    // _pad: [u8; 6] in Rust — CLR packs automatically, no explicit padding needed

    public unsafe string GetName()
    {
        fixed (ushort* p = Name)
        {
            return Marshal.PtrToStringUni((nint)p, (int)NameLen) ?? "";
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct SearchResultNative
{
    public uint MatchCount;
    public uint AncestorCount;
    public nint MatchIds;
    public nint AncestorIds;
}

public static class NativeMethods
{
    private const string DllName = "aqua_core.dll";

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe nint walk_tree(nint* rootPtrs, int count, byte* cancel);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_tree(nint tree);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe int get_children(nint tree, uint nodeId, uint filter, uint* outIds, int outLen);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern NodeInfo get_node_info(nint tree, uint nodeId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe nint search_tree(nint tree, char* query, byte* cancel);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe nint search_tree_chunked(nint tree, char* query, uint startFrom, uint maxScan, byte* cancel);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe char* get_node_full_path(nint tree, uint nodeId);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint get_node_full_path_len();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void free_search_result(nint result);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe byte* last_error_ptr();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint last_error_len();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe byte save_tree_to_cache(nint tree, char* cacheDir, nint* rootPtrs, int rootCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe nint load_tree_from_cache(char* cacheDir, nint* rootPtrs, int rootCount);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern unsafe byte clear_cache(char* cacheDir);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int tree_node_count(nint tree);

    /// <summary>
    /// Get the last error message from the native library.
    /// </summary>
    public static unsafe string? GetLastError()
    {
        var ptr = last_error_ptr();
        var len = last_error_len();
        if (ptr == null || len == 0) return null;
        return System.Text.Encoding.UTF8.GetString(ptr, (int)len);
    }
}
