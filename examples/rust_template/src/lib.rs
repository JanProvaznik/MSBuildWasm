use std::ffi::CString;
use std::os::raw::c_char;

#[repr(C)] #[allow(dead_code)]
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
#[link(wasm_import_module = "msbuild-log")] #[allow(dead_code)]
extern "C" {
    fn LogError(ptr: *const c_char, len: usize);
    fn LogWarning(ptr: *const c_char, len: usize);
    fn LogMessage(messageImportance: MessageImportance, ptr: *const c_char, len: usize);
}

#[link(wasm_import_module = "msbuild-taskinfo")]
extern "C" {
    fn TaskInfo(ptr: *const c_char, len: usize); // this is a ptr to a json string
}



#[no_mangle] #[allow(non_snake_case)]
pub fn Execute() -> TaskResult
{
        // Task input properties in stdin
        let mut input = String::new();
        std::io::stdin().read_line(&mut input).unwrap();

        // let errorMessage = CString::new("Error message from Wasm").unwrap(); // we don't want the template task failing
        // show what the template task got on input
        let warning_message= CString::new(input).unwrap();
        let message1= CString::new("High priority message from Wasm").unwrap();
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
            // task output properties in stdout
            println!("{}", out_json_str.to_str().unwrap());
        }

        return TaskResult::Success;
}

#[no_mangle] #[allow(non_snake_case)]
pub fn GetTaskInfo() 
{
    let c_string = CString::new(r#"{"Properties":{"TestNormalProperty":{"type":"string","required":false,"output":false},"TestOutputProperty":{"type":"string","required":false,"output":true},"TestRequiredProperty":{"type":"string","required":true,"output":false},"TestBoolProperty":{"type":"bool","required":false,"output":false}}}"#).unwrap();
    unsafe 
    {
    TaskInfo(c_string.as_ptr(), c_string.to_bytes().len());
    }
}
