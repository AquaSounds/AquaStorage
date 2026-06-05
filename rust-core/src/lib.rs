mod error;
mod tree;

use std::cell::RefCell;
use std::ffi::OsStr;
use std::os::windows::ffi::OsStrExt;
use tree::FsTree;

// ─── Thread-local path buffer ────────────────────────────────────────────

thread_local! {
    static NODE_PATH_BUF: RefCell<Vec<u16>> = RefCell::new(Vec::new());
}

// ─── Helpers ───────────────────────────────────────────────────────────────

/// Read a null-terminated UTF-16 string from a raw pointer.
unsafe fn read_utf16_str(ptr: *const u16) -> String {
    if ptr.is_null() {
        return String::new();
    }
    let len = (0..).take_while(|&i| *ptr.add(i) != 0).count();
    let slice = std::slice::from_raw_parts(ptr, len);
    String::from_utf16_lossy(slice)
}

/// Read an array of null-terminated UTF-16 strings.
unsafe fn read_utf16_str_array(ptr: *const *const u16, count: i32) -> Vec<String> {
    if ptr.is_null() || count <= 0 {
        return Vec::new();
    }
    let mut result = Vec::with_capacity(count as usize);
    for i in 0..count as usize {
        let s = read_utf16_str(*ptr.add(i));
        if !s.is_empty() {
            result.push(s);
        }
    }
    result
}

// ─── C-compatible types ────────────────────────────────────────────────────

#[repr(C)]
pub struct NodeInfo {
    /// UTF-16 encoded name, null-terminated if shorter than 256
    pub name: [u16; 256],
    /// Actual length of name in u16 code units (excluding null)
    pub name_len: u32,
    pub is_dir: u8,
    pub is_audio: u8,
    pub size_bytes: u64,
    pub child_count: u32,
    pub has_audio_in_subtree: u8,
    pub _pad: [u8; 6],
}

#[repr(C)]
pub struct SearchResult {
    pub match_count: u32,
    pub ancestor_count: u32,
    pub match_ids: *mut u32,
    pub ancestor_ids: *mut u32,
}

impl SearchResult {
    fn new() -> Self {
        SearchResult {
            match_count: 0,
            ancestor_count: 0,
            match_ids: std::ptr::null_mut(),
            ancestor_ids: std::ptr::null_mut(),
        }
    }

    fn set(&mut self, matches: Vec<u32>, ancestors: Vec<u32>) {
        self.match_count = matches.len() as u32;
        self.ancestor_count = ancestors.len() as u32;
        self.match_ids = if matches.is_empty() {
            std::ptr::null_mut()
        } else {
            let mut b = matches.into_boxed_slice();
            let p = b.as_mut_ptr();
            std::mem::forget(b);
            p
        };
        self.ancestor_ids = if ancestors.is_empty() {
            std::ptr::null_mut()
        } else {
            let mut b = ancestors.into_boxed_slice();
            let p = b.as_mut_ptr();
            std::mem::forget(b);
            p
        };
    }
}

// ─── FFI Exports ───────────────────────────────────────────────────────────

/// Walk directory trees. Returns null on error or cancellation (check last_error).
/// `root_ptrs`: array of null-terminated UTF-16 path strings
/// `count`: number of paths in the array
/// `cancel`: pointer to a u8 that triggers cancellation when set to non-zero
#[no_mangle]
pub unsafe extern "C" fn walk_tree(
    root_ptrs: *const *const u16,
    count: i32,
    cancel: *const u8,
) -> *mut FsTree {
    error::clear_error();
    let paths = read_utf16_str_array(root_ptrs, count);
    if paths.is_empty() {
        error::set_error("No valid paths provided".to_string());
        return std::ptr::null_mut();
    }

    match FsTree::walk(&paths, cancel) {
        Ok(tree) => Box::into_raw(Box::new(tree)),
        Err(msg) => {
            if msg == "cancelled" {
                error::set_error("cancelled".to_string());
            } else {
                error::set_error(msg);
            }
            std::ptr::null_mut()
        }
    }
}

/// Free a tree created by walk_tree or load_tree_from_cache.
#[no_mangle]
pub unsafe extern "C" fn free_tree(tree: *mut FsTree) {
    if !tree.is_null() {
        drop(Box::from_raw(tree));
    }
}

/// Get children of a node. Returns total number of children (may exceed out_len).
/// `node_id`: ROOT_ID (u32::MAX) to get root-level children.
/// `filter`: 0 = all, 1 = audio files only
/// Writes up to `out_len` child IDs into `out_ids`.
#[no_mangle]
pub unsafe extern "C" fn get_children(
    tree: *const FsTree,
    node_id: u32,
    filter: u32,
    out_ids: *mut u32,
    out_len: i32,
) -> i32 {
    if tree.is_null() || out_ids.is_null() {
        return -1;
    }
    let tree = &*tree;
    let audio_only = filter == 1;
    let children = tree.get_children(node_id, audio_only);

    let count = children.len().min(out_len.max(0) as usize);
    for i in 0..count {
        *out_ids.add(i) = children[i].0;
    }
    children.len() as i32
}

/// Get info for a single node. Returns zeroed NodeInfo on invalid node_id.
#[no_mangle]
pub unsafe extern "C" fn get_node_info(tree: *const FsTree, node_id: u32) -> NodeInfo {
    if tree.is_null() {
        return zeroed_node_info();
    }
    let tree = &*tree;
    match tree.get_node(node_id) {
        Some(node) => {
            let mut name_buf = [0u16; 256];
            let encoded: Vec<u16> = OsStr::new(&node.name).encode_wide().collect();
            let len = encoded.len().min(255);
            name_buf[..len].copy_from_slice(&encoded[..len]);

            NodeInfo {
                name: name_buf,
                name_len: len as u32,
                is_dir: node.is_dir as u8,
                is_audio: node.is_audio as u8,
                size_bytes: node.size_bytes,
                child_count: node.children.len() as u32,
                has_audio_in_subtree: node.has_audio_in_subtree as u8,
                _pad: [0u8; 6],
            }
        }
        None => zeroed_node_info(),
    }
}

fn zeroed_node_info() -> NodeInfo {
    NodeInfo {
        name: [0u16; 256],
        name_len: 0,
        is_dir: 0,
        is_audio: 0,
        size_bytes: 0,
        child_count: 0,
        has_audio_in_subtree: 0,
        _pad: [0u8; 6],
    }
}

/// Search tree for nodes whose name contains `query` (case-insensitive).
/// Returns null on error. Free with free_search_result.
#[no_mangle]
pub unsafe extern "C" fn search_tree(
    tree: *mut FsTree,
    query: *const u16,
    cancel: *const u8,
) -> *mut SearchResult {
    error::clear_error();
    if tree.is_null() {
        error::set_error("tree is null".to_string());
        return std::ptr::null_mut();
    }

    let query_str = read_utf16_str(query);
    if query_str.is_empty() {
        error::set_error("query is empty".to_string());
        return std::ptr::null_mut();
    }

    let tree = &mut *tree;
    let (matches, ancestors) = tree.search(&query_str, cancel);

    let mut result = Box::new(SearchResult::new());
    let ancestor_vec: Vec<u32> = ancestors.into_iter().collect();
    result.set(matches, ancestor_vec);
    Box::into_raw(result)
}

/// Chunked search: scan nodes in [start_from, start_from + max_scan).
/// Uses cached parent_of index for incremental scanning performance.
/// Returns null on error. Free with free_search_result.
#[no_mangle]
pub unsafe extern "C" fn search_tree_chunked(
    tree: *mut FsTree,
    query: *const u16,
    start_from: u32,
    max_scan: u32,
    cancel: *const u8,
) -> *mut SearchResult {
    error::clear_error();
    if tree.is_null() {
        error::set_error("tree is null".to_string());
        return std::ptr::null_mut();
    }

    let query_str = read_utf16_str(query);
    if query_str.is_empty() {
        error::set_error("query is empty".to_string());
        return std::ptr::null_mut();
    }

    if max_scan == 0 {
        return std::ptr::null_mut();
    }

    let tree = &mut *tree;
    let (matches, ancestors) = tree.search_chunked(&query_str, start_from, max_scan, cancel);

    let mut result = Box::new(SearchResult::new());
    let ancestor_vec: Vec<u32> = ancestors.into_iter().collect();
    result.set(matches, ancestor_vec);
    Box::into_raw(result)
}

/// Get full path for a node. Returns null if invalid node_id.
/// Valid until next FFI call on this thread.
#[no_mangle]
pub unsafe extern "C" fn get_node_full_path(tree: *const FsTree, node_id: u32) -> *const u16 {
    if tree.is_null() {
        return std::ptr::null();
    }
    let tree = &*tree;
    match tree.get_node(node_id) {
        Some(node) => {
            NODE_PATH_BUF.with(|buf| {
                let mut buf = buf.borrow_mut();
                buf.clear();
                buf.extend(OsStr::new(&node.path).encode_wide());
                buf.push(0);
                buf.as_ptr()
            })
        }
        None => std::ptr::null(),
    }
}

/// Length of the path returned by get_node_full_path, in u16 code units (excluding null).
#[no_mangle]
pub unsafe extern "C" fn get_node_full_path_len() -> u32 {
    NODE_PATH_BUF.with(|buf| {
        let buf = buf.borrow();
        if buf.is_empty() { 0 } else { (buf.len() - 1) as u32 }
    })
}

/// Free a search result.
#[no_mangle]
pub unsafe extern "C" fn free_search_result(result: *mut SearchResult) {
    if !result.is_null() {
        let r = Box::from_raw(result);
        if !r.match_ids.is_null() {
            drop(Box::from_raw(std::slice::from_raw_parts_mut(
                r.match_ids,
                r.match_count as usize,
            )));
        }
        if !r.ancestor_ids.is_null() {
            drop(Box::from_raw(std::slice::from_raw_parts_mut(
                r.ancestor_ids,
                r.ancestor_count as usize,
            )));
        }
    }
}

/// Get pointer to last error UTF-8 string. Valid until next FFI call on this thread.
/// Returns null if no error.
#[no_mangle]
pub unsafe extern "C" fn last_error_ptr() -> *const u8 {
    error::error_ptr()
}

/// Get length of last error string in bytes.
#[no_mangle]
pub unsafe extern "C" fn last_error_len() -> u32 {
    error::error_len()
}

/// Save tree to cache. Returns 0 on success, non-zero on error.
#[no_mangle]
pub unsafe extern "C" fn save_tree_to_cache(
    tree: *const FsTree,
    cache_dir: *const u16,
    root_ptrs: *const *const u16,
    root_count: i32,
) -> u8 {
    error::clear_error();
    if tree.is_null() {
        error::set_error("tree is null".to_string());
        return 1;
    }
    let cache_dir_str = read_utf16_str(cache_dir);
    let root_paths = read_utf16_str_array(root_ptrs, root_count);

    let tree = &*tree;
    match tree.save_to_cache(&cache_dir_str, &root_paths) {
        Ok(()) => 0,
        Err(msg) => {
            error::set_error(msg);
            1
        }
    }
}

/// Load tree from cache. Returns null if no cache or cache is stale.
#[no_mangle]
pub unsafe extern "C" fn load_tree_from_cache(
    cache_dir: *const u16,
    root_ptrs: *const *const u16,
    root_count: i32,
) -> *mut FsTree {
    error::clear_error();
    let cache_dir_str = read_utf16_str(cache_dir);
    let root_paths = read_utf16_str_array(root_ptrs, root_count);

    match FsTree::load_from_cache(&cache_dir_str, &root_paths) {
        Ok(Some(tree)) => Box::into_raw(Box::new(tree)),
        Ok(None) => std::ptr::null_mut(),
        Err(msg) => {
            error::set_error(msg);
            std::ptr::null_mut()
        }
    }
}

/// Clear all cache files.
#[no_mangle]
pub unsafe extern "C" fn clear_cache(cache_dir: *const u16) -> u8 {
    error::clear_error();
    let cache_dir_str = read_utf16_str(cache_dir);
    match FsTree::clear_cache(&cache_dir_str) {
        Ok(()) => 0,
        Err(msg) => {
            error::set_error(msg);
            1
        }
    }
}

/// Get total node count in tree.
#[no_mangle]
pub unsafe extern "C" fn tree_node_count(tree: *const FsTree) -> i32 {
    if tree.is_null() {
        return -1;
    }
    let tree = &*tree;
    tree.node_count() as i32
}

// ─── Tests ─────────────────────────────────────────────────────────────────

#[cfg(test)]
mod tests {
    use super::*;
    use tree::ROOT_ID;
    use std::fs;

    fn create_test_fs(dir: &std::path::Path) -> std::path::PathBuf {
        let root = dir.join("root");
        fs::create_dir_all(root.join("sub1")).unwrap();
        fs::create_dir_all(root.join("sub2")).unwrap();
        fs::write(root.join("sub1").join("a.wav"), b"fake").unwrap();
        fs::write(root.join("sub1").join("b.mp3"), b"fake").unwrap();
        fs::write(root.join("sub2").join("c.txt"), b"fake").unwrap();
        fs::write(root.join("d.flac"), b"fake").unwrap();
        root
    }

    fn test_walk() -> (FsTree, std::path::PathBuf) {
        let dir = std::env::temp_dir().join(format!("aqua_test_{}", uuid()));
        fs::create_dir_all(&dir).unwrap();
        let root = create_test_fs(&dir);
        let tree = FsTree::walk(&[root.to_string_lossy().to_string()], std::ptr::null()).unwrap();
        (tree, dir)
    }

    fn uuid() -> u64 {
        use std::sync::atomic::AtomicU64;
        static COUNTER: AtomicU64 = AtomicU64::new(0);
        COUNTER.fetch_add(1, std::sync::atomic::Ordering::Relaxed)
    }

    #[test]
    fn test_walk_tree() {
        let (tree, dir) = test_walk();
        assert!(tree.node_count() >= 6);

        let children = tree.get_children(ROOT_ID, false);
        assert_eq!(children.len(), 1);

        let (root_id, root_node) = children[0];
        assert!(root_node.is_dir);

        let root_children = tree.get_children(root_id, false);
        assert!(root_children.len() >= 2);

        let audio_children = tree.get_children(root_id, true);
        assert!(audio_children.iter().any(|(_, n)| n.name == "d.flac"));

        for &(id, _) in &root_children {
            let info = tree.get_node(id).unwrap();
            if info.name == "sub1" {
                assert!(info.has_audio_in_subtree);
            }
        }

        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn test_search() {
        let (mut tree, dir) = test_walk();
        let (matches, ancestors) = tree.search("a.wav", std::ptr::null());
        assert!(!matches.is_empty());
        assert!(!ancestors.is_empty());
        let _ = fs::remove_dir_all(&dir);
    }

    #[test]
    fn test_cache_roundtrip() {
        let (tree, dir) = test_walk();
        let count = tree.node_count();

        let cache_dir = dir.join("cache");
        let root_paths = vec![dir.join("root").to_string_lossy().to_string()];
        tree.save_to_cache(&cache_dir.to_string_lossy(), &root_paths)
            .unwrap();

        let loaded = FsTree::load_from_cache(&cache_dir.to_string_lossy(), &root_paths)
            .unwrap()
            .unwrap();

        assert_eq!(loaded.node_count(), count);
        let _ = fs::remove_dir_all(&dir);
    }
}
