using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AquaStorage.Helpers;

public sealed class FsTreeHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public FsTreeHandle() : base(true) { }

    public FsTreeHandle(nint handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.free_tree(handle);
        return true;
    }
}

public sealed class SearchResultHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SearchResultHandle() : base(true) { }

    public SearchResultHandle(nint handle) : base(true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        NativeMethods.free_search_result(handle);
        return true;
    }
}
