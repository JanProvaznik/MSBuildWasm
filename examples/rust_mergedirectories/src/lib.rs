use std::ffi::CString;
use std::path::Path;
use std::os::raw::c_char;
use std::fs;
use std::io;
use serde_json::Value;
use serde::Deserialize;

#[repr(C)] #[allow(dead_code)]
pub enum MessageImportance {
    High,
    Normal,
    Low,
}

#[repr(C)] #[allow(dead_code)]
pub enum TaskResult {
    Success,  
    Failure,
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

#[derive(serde::Deserialize)]
struct Properties {
    Dirs: Vec<Value>,
}

#[derive(serde::Deserialize)]
struct TaskInput {
    Properties: Properties,
}

#[no_mangle]
#[allow(non_snake_case)]
pub fn Execute() -> TaskResult {
    // Task input properties in stdin
    let mut input = String::new();
    std::io::stdin().read_line(&mut input).unwrap();
    let taskInput: TaskInput = serde_json::from_str(&input).unwrap();

    let wasm_paths: Vec<String> = taskInput 
        .Properties
        .Dirs
        .iter()
        .filter_map(|dir| dir.get("WasmPath"))
        .filter_map(|wasm_path| wasm_path.as_str())
        .map(|s| s.to_string())
        .collect();
    merge_directories(&wasm_paths, "output_dir").unwrap();

    for path in wasm_paths {
        log_warning(&format!("WasmPath: {}", path));
    }

    println!("{}", r#"{"MergedDir":{"ItemSpec":"output_dir","WasmPath":"output_dir"}}"#);
    return TaskResult::Success;
}

#[no_mangle]
#[allow(non_snake_case)]
pub fn GetTaskInfo() {
    task_info(r#"{"Properties":{"Dirs":{"type":"ITaskItem[]","required":true,"output":false},"MergedDir":{"type":"ITaskItem","required":false,"output":true},"MergedName":{"type":"string","required":false,"output":false}}}"#);
}

fn merge_directories(paths: &[String], output_name: &str) -> io::Result<()> {
    let output_dir = Path::new(output_name);
    fs::create_dir_all(output_dir)?;

    for path_str in paths {
        let path = Path::new(path_str);
        if path.is_dir() {
            for entry in fs::read_dir(path)? {
                let entry = entry?;
                let entry_path = entry.path();
                let relative_path = entry_path.strip_prefix(path).unwrap();
                let target_path = output_dir.join(relative_path);

                if entry_path.is_dir() {
                    fs::create_dir_all(&target_path)?;
                } else {
                    if let Some(parent) = target_path.parent() {
                        fs::create_dir_all(parent)?;
                    }
                    fs::copy(&entry_path, &target_path)?;
                }
            }
        }
    }

    Ok(())
}