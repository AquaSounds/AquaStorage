use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::fs;
use std::path::Path;

const AUDIO_EXTENSIONS: &[&str] = &["wav", "mp3", "ogg", "flac"];

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FsNode {
    pub name: String,
    pub path: String,
    pub is_dir: bool,
    pub is_audio: bool,
    pub size_bytes: u64,
    pub children: Vec<u32>,
    pub has_audio_in_subtree: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CacheMeta {
    pub root_paths: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FsTree {
    pub nodes: Vec<FsNode>,
    pub root_indices: Vec<u32>,
}

/// Sentinel for "no parent" / "root level" queries
pub const ROOT_ID: u32 = u32::MAX;

fn is_audio_file(name: &str) -> bool {
    name.rfind('.')
        .map(|i| {
            let ext = &name[i + 1..];
            AUDIO_EXTENSIONS.iter().any(|&e| e.eq_ignore_ascii_case(ext))
        })
        .unwrap_or(false)
}

impl FsTree {
    pub fn new() -> Self {
        FsTree {
            nodes: Vec::new(),
            root_indices: Vec::new(),
        }
    }

    fn alloc_node(&mut self, node: FsNode) -> u32 {
        let id = self.nodes.len() as u32;
        self.nodes.push(node);
        id
    }

    /// Walk a single root directory, returning the root node index.
    /// `cancel` may be null; if non-null, checked every 1000 entries.
    fn walk_root(
        &mut self,
        root: &Path,
        cancel: *const u8,
    ) -> Option<u32> {
        if !root.is_dir() {
            return None;
        }

        let root_name = root
            .file_name()
            .map(|s| s.to_string_lossy().to_string())
            .unwrap_or_else(|| root.to_string_lossy().to_string());
        let root_path = root.to_string_lossy().to_string();
        let root_is_audio = false;

        // Collect all entries first so we can compute has_audio_in_subtree bottom-up
        #[derive(Debug)]
        struct RawEntry {
            name: String,
            path: String,
            is_dir: bool,
            is_audio: bool,
            size_bytes: u64,
            parent_idx: u32, // index into entries vec (ROOT_ID = root)
            idx: u32,        // index into entries vec
        }

        let mut entries: Vec<RawEntry> = Vec::new();

        // Use a stack for DFS so parent tracking works
        // Stack items: (parent_idx, dir_path)
        struct StackItem {
            parent_idx: u32,
            dir_path: std::path::PathBuf,
        }

        let root_entry = RawEntry {
            name: root_name,
            path: root_path,
            is_dir: true,
            is_audio: root_is_audio,
            size_bytes: 0,
            parent_idx: ROOT_ID,
            idx: 0,
        };
        entries.push(root_entry);
        let root_idx: u32 = 0;

        let mut stack: Vec<StackItem> = Vec::new();
        stack.push(StackItem {
            parent_idx: root_idx,
            dir_path: root.to_path_buf(),
        });

        let mut iter_count: u64 = 0;

        while let Some(item) = stack.pop() {
            let parent_idx = item.parent_idx;
            let dir = item.dir_path;

            let read_result = fs::read_dir(&dir);
            let dir_entries = match read_result {
                Ok(rd) => rd,
                Err(_) => continue,
            };

            for entry in dir_entries {
                iter_count += 1;
                if iter_count % 1000 == 0 {
                    if !cancel.is_null() && unsafe { *cancel != 0 } {
                        return None;
                    }
                }

                let entry = match entry {
                    Ok(e) => e,
                    Err(_) => continue,
                };

                let ft = match entry.file_type() {
                    Ok(t) => t,
                    Err(_) => continue,
                };

                let name = entry.file_name().to_string_lossy().to_string();

                // Skip hidden/system on Windows
                if name.starts_with('.') {
                    continue;
                }

                let path = entry.path().to_string_lossy().to_string();

                if ft.is_dir() {
                    // Check if it's hidden/system via filename convention (Windows)
                    // walkdir handles hidden filtering better, but we're using std::fs
                    let idx = entries.len() as u32;
                    entries.push(RawEntry {
                        name,
                        path,
                        is_dir: true,
                        is_audio: false,
                        size_bytes: 0,
                        parent_idx,
                        idx,
                    });
                    stack.push(StackItem {
                        parent_idx: idx,
                        dir_path: entry.path(),
                    });
                } else if ft.is_file() {
                    let is_audio = is_audio_file(&entry.file_name().to_string_lossy());
                    let size = entry.metadata().map(|m| m.len()).unwrap_or(0);

                    let idx = entries.len() as u32;
                    entries.push(RawEntry {
                        name,
                        path,
                        is_dir: false,
                        is_audio,
                        size_bytes: size,
                        parent_idx,
                        idx,
                    });
                }
            }
        }

        // Re-check cancellation
        if !cancel.is_null() && unsafe { *cancel != 0 } {
            return None;
        }

        // Build child lists and compute has_audio_in_subtree bottom-up
        // Process in reverse order so children come before parents
        let n = entries.len();
        let mut has_audio: Vec<bool> = vec![false; n];
        let mut children: Vec<Vec<u32>> = vec![Vec::new(); n];
        let mut node_ids: Vec<u32> = vec![0; n];

        for entry in entries.into_iter() {
            let id = self.alloc_node(FsNode {
                name: entry.name,
                path: entry.path,
                is_dir: entry.is_dir,
                is_audio: entry.is_audio,
                size_bytes: entry.size_bytes,
                children: Vec::new(), // filled below
                has_audio_in_subtree: false, // filled below
            });
            node_ids[entry.idx as usize] = id;

            if entry.is_audio {
                has_audio[entry.idx as usize] = true;
            }

            if entry.parent_idx != ROOT_ID {
                children[entry.parent_idx as usize].push(entry.idx);
            }
        }

        // Propagate has_audio_in_subtree bottom-up
        for i in (0..n).rev() {
            let node_has_audio = has_audio[i];
            let child_ids = &children[i];
            let mut subtree_has_audio = node_has_audio;
            for &child_idx in child_ids {
                if has_audio[child_idx as usize] {
                    subtree_has_audio = true;
                }
            }
            has_audio[i] = subtree_has_audio;

            let actual_id = node_ids[i];
            let child_actual_ids: Vec<u32> = child_ids.iter().map(|&c| node_ids[c as usize]).collect();
            self.nodes[actual_id as usize].children = child_actual_ids;
            self.nodes[actual_id as usize].has_audio_in_subtree = subtree_has_audio;
        }

        let actual_root_id = node_ids[root_idx as usize];
        self.root_indices.push(actual_root_id);

        Some(actual_root_id)
    }

    pub fn walk(
        root_paths: &[String],
        cancel: *const u8,
    ) -> Result<Self, String> {
        let mut tree = FsTree::new();

        for root_path in root_paths {
            let root = Path::new(root_path);
            if !root.exists() {
                return Err(format!("Path not found: {}", root_path));
            }
            if tree.walk_root(root, cancel).is_none() {
                // Cancelled
                return Err("cancelled".to_string());
            }
        }

        Ok(tree)
    }

    pub fn node_count(&self) -> usize {
        self.nodes.len()
    }

    pub fn get_children(&self, node_id: u32, audio_only: bool) -> Vec<(u32, &FsNode)> {
        if node_id == ROOT_ID {
            return self
                .root_indices
                .iter()
                .map(|&id| (id, &self.nodes[id as usize]))
                .collect();
        }

        let node = &self.nodes[node_id as usize];
        node.children
            .iter()
            .filter_map(|&child_id| {
                let child = &self.nodes[child_id as usize];
                if audio_only && !child.is_dir && !child.is_audio {
                    return None;
                }
                Some((child_id, child))
            })
            .collect()
    }

    pub fn get_node(&self, node_id: u32) -> Option<&FsNode> {
        self.nodes.get(node_id as usize)
    }

    /// Search for nodes whose name contains `query` (case-insensitive).
    /// Returns (matching_node_ids, ancestor_ids_to_expand).
    pub fn search(
        &self,
        query: &str,
        cancel: *const u8,
    ) -> (Vec<u32>, HashSet<u32>) {
        let query_lower = query.to_lowercase();
        let mut matches: Vec<u32> = Vec::new();
        let mut ancestors: HashSet<u32> = HashSet::new();

        // Collect parent pointers
        let mut parent_of: Vec<u32> = vec![ROOT_ID; self.nodes.len()];
        for (i, node) in self.nodes.iter().enumerate() {
            for &child in &node.children {
                parent_of[child as usize] = i as u32;
            }
        }
        for &root_idx in &self.root_indices {
            parent_of[root_idx as usize] = ROOT_ID;
        }

        let mut count: u64 = 0;
        for (i, node) in self.nodes.iter().enumerate() {
            count += 1;
            if count % 5000 == 0 {
                if !cancel.is_null() && unsafe { *cancel != 0 } {
                    return (Vec::new(), HashSet::new());
                }
            }

            if node.name.to_lowercase().contains(&query_lower) {
                matches.push(i as u32);
                // Walk up to mark ancestors
                let mut p = parent_of[i];
                while p != ROOT_ID {
                    ancestors.insert(p);
                    p = parent_of[p as usize];
                }
            }
        }

        (matches, ancestors)
    }
}

/// Cache I/O — MessagePack serialization
impl FsTree {
    pub fn save_to_cache(
        &self,
        cache_dir: &str,
        root_paths: &[String],
    ) -> Result<(), String> {
        let dir = Path::new(cache_dir);
        fs::create_dir_all(dir)
            .map_err(|e| format!("Failed to create cache dir: {}", e))?;

        let tree_path = dir.join("FolderTree");
        let meta = CacheMeta {
            root_paths: root_paths.to_vec(),
        };

        let mut data = Vec::new();
        rmp_serde::encode::write_named(&mut data, &meta)
            .map_err(|e| format!("Failed to encode cache meta: {}", e))?;
        rmp_serde::encode::write_named(&mut data, &self)
            .map_err(|e| format!("Failed to encode cache tree: {}", e))?;

        fs::write(&tree_path, &data)
            .map_err(|e| format!("Failed to write cache: {}", e))?;

        Ok(())
    }

    pub fn load_from_cache(
        cache_dir: &str,
        expected_roots: &[String],
    ) -> Result<Option<Self>, String> {
        let tree_path = Path::new(cache_dir).join("FolderTree");
        if !tree_path.exists() {
            return Ok(None);
        }

        let data = fs::read(&tree_path)
            .map_err(|e| format!("Failed to read cache: {}", e))?;

        let mut cursor = &data[..];

        let meta: CacheMeta = rmp_serde::decode::from_read(&mut cursor)
            .map_err(|e| format!("Failed to decode cache meta: {}", e))?;

        // Validate root paths match
        let saved: HashSet<&str> = meta.root_paths.iter().map(|s| s.as_str()).collect();
        let current: HashSet<&str> = expected_roots.iter().map(|s| s.as_str()).collect();
        if saved != current {
            return Ok(None);
        }

        let tree: FsTree = rmp_serde::decode::from_read(&mut cursor)
            .map_err(|e| format!("Failed to decode cache tree: {}", e))?;

        Ok(Some(tree))
    }

    pub fn clear_cache(cache_dir: &str) -> Result<(), String> {
        let dir = Path::new(cache_dir);
        if dir.exists() {
            fs::remove_dir_all(dir)
                .map_err(|e| format!("Failed to clear cache: {}", e))?;
        }
        Ok(())
    }
}
