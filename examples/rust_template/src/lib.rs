use std::ffi::CString;
use std::os::raw::c_char;

#[repr(C)]
enum MessageImportance {
    High,
    Normal,
    Low
}

// Import logging as functions from the host environment
#[link(wasm_import_module = "msbuild-log")]
extern "C" {
    fn LogError(ptr: *const c_char, len: usize);
    fn LogWarning(ptr: *const c_char, len: usize);  
    fn LogMessage(messageImportance: MessageImportance, ptr: *const c_char, len: usize);
}

#[no_mangle]
pub fn execute()
{
        // println!("Hello from WebAssembly output");
        // let errorMessage = CString::new("Error message from Wasm").unwrap();
        // let warningMessage = CString::new("Warning message from Wasm").unwrap();
        // let message1 = CString::new("High priority message from Wasm").unwrap();
        // let message2 = CString::new("Normal priority message from Wasm").unwrap();
        // let message3 = CString::new("Low priority message from Wasm").unwrap();

        unsafe
        {
            // LogError(errorMessage.as_ptr(), errorMessage.to_bytes().len());
            // LogWarning(warningMessage.as_ptr(), warningMessage.to_bytes().len());
            // LogMessage(MessageImportance::High, message1.as_ptr(), message1.to_bytes().len());
            // LogMessage(MessageImportance::High, message2.as_ptr(), message2.to_bytes().len());
            // LogMessage(MessageImportance::High, message3.as_ptr(), message3.to_bytes().len());

        }
}