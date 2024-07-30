use std::ffi::CString;
use std::os::raw::c_char;

#[repr(C)] #[allow(dead_code)]
pub enum MessageImportance {
    High,
    Normal,
    Low
}

#[repr(C)] #[allow(dead_code)]
pub enum TaskResult {
    Success,  
    Failure 
}

// Import logging as functions from the host environment
#[link(wasm_import_module = "msbuild-log")] #[allow(dead_code)]
extern "C" {
    fn LogError(message: *const c_char, message_length: usize);
    fn LogWarning(message: *const c_char, message_legth: usize);  
    fn LogMessage(messageImportance: MessageImportance, message: *const c_char, message_length: usize);
}

#[allow(dead_code)]
fn log_message(messageImportance: MessageImportance, message: &str) {
    let c_message = CString::new(message).unwrap();
    unsafe {
        LogMessage(messageImportance, c_message.as_ptr(), c_message.to_bytes().len());
    }
}
#[allow(dead_code)]
fn log_error(message: &str) {
    let c_message = CString::new(message).unwrap();
    unsafe {
        LogError(c_message.as_ptr(), c_message.to_bytes().len());
    }
}
#[allow(dead_code)]
fn log_warning(message: &str) {
    let c_message = CString::new(message).unwrap();
    unsafe {
        LogWarning(c_message.as_ptr(), c_message.to_bytes().len());
    }
}

#[link(wasm_import_module = "msbuild-taskinfo")]
extern "C" {
    fn TaskInfo(task_info_json: *const c_char, task_info_length: usize); // this is a ptr to a json string
}

fn task_info(task_info_json: &str) {
    let c_message = CString::new(task_info_json).unwrap();
    unsafe {
        TaskInfo(c_message.as_ptr(), c_message.to_bytes().len());
    }
}

#[no_mangle] #[allow(non_snake_case)]
pub fn Execute() -> TaskResult
{
        // Task input properties in stdin
        let mut input = String::new();
        std::io::stdin().read_line(&mut input).unwrap();

        
        // log_error("Error message from Wasm"); // we don't want the template task failing
        // show what the template task got on input
        log_warning(&input);
        log_message(MessageImportance::High, "High priority message from Wasm");
        log_message(MessageImportance::Normal, "Normal priority message from Wasm");
        log_message(MessageImportance::Low, "Low priority message from Wasm");
        
        
        println!("{}",r#"{"TestOutputProperty":"This is the output property value from WASM task"}"#);

        return TaskResult::Success;
}

#[no_mangle] #[allow(non_snake_case)]
pub fn GetTaskInfo() 
{
    task_info(r#"{"Properties":{"TestNormalProperty":{"type":"string","required":false,"output":false},"TestOutputProperty":{"type":"string","required":false,"output":true},"TestRequiredProperty":{"type":"string","required":true,"output":false},"TestBoolProperty":{"type":"bool","required":false,"output":false}}}"#);
}
