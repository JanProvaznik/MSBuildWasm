use std::ffi::CString;
use std::ffi::c_char;

#[repr(C)] #[allow(dead_code)]
pub enum MessageImportance {
    High,
    Normal,
    Low
}

// Log a message with the specified importance
#[allow(dead_code)]
pub fn log_message(message_importance: MessageImportance, message: &str) {
    let c_message = CString::new(message).unwrap();
    unsafe {
        LogMessage(message_importance, c_message.as_ptr(), c_message.to_bytes().len());
    }
}

// Log an error in MSBuild, this will cause the build to fail.
#[allow(dead_code)]
pub fn log_error(message: &str) {
    let c_message = CString::new(message).unwrap();
    unsafe {
        LogError(c_message.as_ptr(), c_message.to_bytes().len());
    }
}

// Log a warning in MSBuild
#[allow(dead_code)]
pub fn log_warning(message: &str) {
    let c_message = CString::new(message).unwrap();
    unsafe {
        LogWarning(c_message.as_ptr(), c_message.to_bytes().len());
    }
}

// Import logging as functions from the host environment
#[link(wasm_import_module = "msbuild-log")] #[allow(dead_code)]
extern "C" {
    pub fn LogError(message: *const c_char, message_length: usize);
    pub fn LogWarning(message: *const c_char, message_legth: usize);  
    pub fn LogMessage(message_importance: MessageImportance, message: *const c_char, message_length: usize);
}