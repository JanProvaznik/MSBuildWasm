use std::ffi::CString;
use std::os::raw::c_char;

#[repr(C)]
enum MessageImportance {
    High,
    Normal,
    Low
}

#[repr(C)]
pub enum TaskResult {
    Success,  
    Failure 
}

// Import logging as functions from the host environment
#[link(wasm_import_module = "msbuild-log")]
extern "C" {
    fn LogError(ptr: *const c_char, len: usize);
    fn LogWarning(ptr: *const c_char, len: usize);  
    fn LogMessage(messageImportance: MessageImportance, ptr: *const c_char, len: usize);
}

// Import output as a function the host environment
#[link(wasm_import_module = "msbuild-output")]
extern "C" {
    fn Output(ptr: *const c_char, len: usize); // this is a ptr to a json string
}

#[link(wasm_import_module = "msbuild-taskinfo")]
extern "C" {
    fn TaskInfo(ptr: *const c_char, len: usize); // this is a ptr to a json string
}



#[no_mangle]
pub fn execute() -> TaskResult
{
        // read inputs from the environment variable MSBuildInput, it's a json string
        let input_json = std::env::var("MSBuildInput").unwrap();




        // read information about and MSBuildEnv
        // TODO

        println!("Hello from WebAssembly output");
        // let errorMessage = CString::new("Error message from Wasm").unwrap(); // we don't want the task failing
        let warning_message = CString::new("Warning message from Wasm").unwrap();
        let message1 = CString::new(input_json).unwrap();
        let message2 = CString::new("Normal priority message from Wasm").unwrap();
        let message3 = CString::new("Low priority message from Wasm").unwrap();
        let out_json_str = CString::new(r#"{"TestOutputProperty":"This is the output property value from WASM task"}"#).unwrap();
        unsafe
        {
            // LogError(errorMessage.as_ptr(), errorMessage.to_bytes().len());
            LogWarning(warning_message.as_ptr(), warning_message.to_bytes().len());
            LogMessage(MessageImportance::High, message1.as_ptr(), message1.to_bytes().len());
            LogMessage(MessageImportance::Normal, message2.as_ptr(), message2.to_bytes().len());
            LogMessage(MessageImportance::Low, message3.as_ptr(), message3.to_bytes().len());
            Output(out_json_str.as_ptr(), out_json_str.to_bytes().len());
        }

        

        return TaskResult::Success;
}

// Wrapper: ptr, len
// communicate by passing a pointer to wrapper and then the host can offset to get len ???

// struct ReadWrapper {
//     ptr: *const c_char,
//     len: usize
// }

#[no_mangle]
pub fn GetTaskInfo() // -> *const c_char
{
    // Create a CString from the JSON string
    let c_string = CString::new(r#"{"Properties":{"TestNormalProperty":{"type":"string","required":false,"output":false},"TestOutputProperty":{"type":"string","required":false,"output":true},"TestRequiredProperty":{"type":"string","required":true,"output":false},"TestBoolProperty":{"type":"bool","required":false,"output":false}}}"#).unwrap();
    unsafe 
    {
    TaskInfo(c_string.as_ptr(), c_string.to_bytes().len());
    }
    // let taskName = CString::new("This is a description of RustWasmTask").unwrap();
    // let readWrapper = ReadWrapper {
    //     ptr: taskName.as_ptr(),
    //     len: taskName.to_bytes().len()
    // };
    // let readWrapperBox = Box::new(readWrapper);
    // return Box::into_raw(readWrapperBox) as *const c_char;
}
