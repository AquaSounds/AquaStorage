using System.Collections.Generic;

namespace AquaStorage.Models;

public class TreeNodeData
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public List<TreeNodeData> Children { get; set; } = new();
}

public class FolderTreeCache
{
    public List<string> RootPaths { get; set; } = new();
    public List<TreeNodeData> Roots { get; set; } = new();
}
