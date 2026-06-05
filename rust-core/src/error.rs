use std::cell::RefCell;

thread_local! {
    static LAST_ERROR: RefCell<Option<String>> = const { RefCell::new(None) };
}

pub fn set_error(msg: String) {
    LAST_ERROR.with(|e| *e.borrow_mut() = Some(msg));
}

pub fn clear_error() {
    LAST_ERROR.with(|e| *e.borrow_mut() = None);
}

pub fn error_ptr() -> *const u8 {
    LAST_ERROR.with(|e| {
        let e = e.borrow();
        match &*e {
            Some(s) => s.as_ptr(),
            None => std::ptr::null(),
        }
    })
}

pub fn error_len() -> u32 {
    LAST_ERROR.with(|e| {
        let e = e.borrow();
        match &*e {
            Some(s) => s.len() as u32,
            None => 0,
        }
    })
}
