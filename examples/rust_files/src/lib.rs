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
        // the input will be in the form {"Properties:{InputFile1:{Path:"..."},InputFile2:{Path:"..."},OutputFile:{Path:"..."}}}
        let mut input = String::new();
        std::io::stdin().read_line(&mut input).unwrap();

        // Reading and logging directory contents
        match std::fs::read_dir(".") {
            Ok(entries) => {
                for entry in entries {
                    if let Ok(entry) = entry {
                        let file_name = entry.file_name();
                        if let Ok(c_string) = CString::new(file_name.to_string_lossy().as_bytes()) {
                            unsafe{LogWarning( c_string.as_ptr(), c_string.to_bytes().len());}
                        }
                    }
                }
            }
            Err(e) => {
                let error_message = CString::new(format!("Failed to read directory: {}", e)).unwrap();
                unsafe{LogError(error_message.as_ptr(), error_message.to_bytes().len());}
            }
        }
        
        // this is not nice rust code, it lacks error handling
        let input_json: serde_json::Value = serde_json::from_str(&input).unwrap();

        let warning_message= CString::new(input).unwrap();
        unsafe
        {
            LogWarning(warning_message.as_ptr(), warning_message.to_bytes().len());
        }

        // we will parse out the paths from the input string
        let input_file1 = input_json["Properties"]["InputFile1"]["ItemSpec"].as_str().unwrap();
        let input_file2 = input_json["Properties"]["InputFile2"]["ItemSpec"].as_str().unwrap();
        
        // read the contents of the input files
        let input_file1_contents = std::fs::read_to_string(input_file1).unwrap();
        let input_file2_contents = std::fs::read_to_string(input_file2).unwrap();

         // concat
        let output_contents = format!("{}{}", input_file1_contents, input_file2_contents);
         // write to output file
        std::fs::write("wasmconcatoutput.txt", output_contents).unwrap();


        let out_json_str = CString::new(r#"{"OutputFile":{"ItemSpec":"wasmconcatoutput.txt"}}"#).unwrap();
            // task output properties in stdout
        println!("{}", out_json_str.to_str().unwrap());

        return TaskResult::Success;
}

#[no_mangle] #[allow(non_snake_case)]
pub fn GetTaskInfo() 
{
    let c_string = CString::new(r#"{"Properties":{"InputFile1":{"type":"ITaskItem","required":true,"output":false},"InputFile2":{"type":"ITaskItem","required":true,"output":false},"OutputFile":{"type":"ITaskItem","required":false,"output":true}}}"#).unwrap();
    unsafe 
    {
    TaskInfo(c_string.as_ptr(), c_string.to_bytes().len());
    }
}
