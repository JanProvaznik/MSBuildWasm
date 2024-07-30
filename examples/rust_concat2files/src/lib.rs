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
        // the input will be in the form {"Properties:{InputFile1:{Path:"..."},InputFile2:{Path:"..."},OutputFile:{Path:"..."}}}
        let mut input = String::new();
        std::io::stdin().read_line(&mut input).unwrap();

        
        // this is not nice rust code, it lacks error handling
        let input_json: serde_json::Value = serde_json::from_str(&input).unwrap();

        let warning_message= CString::new(input).unwrap();
        unsafe
        {
            LogWarning(warning_message.as_ptr(), warning_message.to_bytes().len());
        }

        // we will parse out the paths from the input string
        let input_file1 = input_json["Properties"]["InputFile1"]["WasmPath"].as_str().unwrap();
        let input_file2 = input_json["Properties"]["InputFile2"]["WasmPath"].as_str().unwrap();
        
        // read the contents of the input files
        let input_file1_contents = std::fs::read_to_string(input_file1).unwrap();
        let input_file2_contents = std::fs::read_to_string(input_file2).unwrap();

         // concat
        let output_contents = format!("{}{}", input_file1_contents, input_file2_contents);
         // write to output file
        std::fs::write("wasmconcatoutput.txt", output_contents).unwrap();
        let out_json_str = r#"{"OutputFile":{"ItemSpec":"wasmconcatoutput.txt","WasmPath":"wasmconcatoutput.txt"}}"#;
        println!("{}", out_json_str);

        return TaskResult::Success;
}

#[no_mangle] #[allow(non_snake_case)]
pub fn GetTaskInfo() 
{
    task_info(r#"{"Properties":{"InputFile1":{"type":"ITaskItem","required":true,"output":false},"InputFile2":{"type":"ITaskItem","required":true,"output":false},"OutputFile":{"type":"ITaskItem","required":false,"output":true}}}"#);
}
