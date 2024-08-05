use std::ffi::CString;
use std::ffi::c_char;
use serde::{Serialize, Deserialize};
use super::logging::{log_message, MessageImportance};

#[repr(C)] #[allow(dead_code)]
pub enum TaskResult {
    Success,  
    Failure 
}

#[derive(Debug, Serialize, Deserialize)]
pub enum PropertyType {
    String,
    Bool,
    ITaskItem,
    ITaskItemArray,
    StringArray,
    BoolArray,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct Property {
    pub name: String,
    pub output: bool,
    pub required: bool,
    pub property_type: PropertyType,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct TaskInfoStruct {
    pub name: String,
    pub properties: Vec<Property>,
}

#[link(wasm_import_module = "msbuild-taskinfo")]
extern "C" {
    pub fn TaskInfo(task_info_json: *const c_char, task_info_length: usize); // this is a ptr to a json string
}

// This function is used to send the task info to the host environment
pub fn task_info(task_info_struct: TaskInfoStruct) {
    let c_message = CString::new(serde_json::to_string(&task_info_struct).unwrap()).unwrap();
    unsafe {
        log_message(MessageImportance::Low, &format!("TaskInfo: {}", c_message.to_str().unwrap()));
        TaskInfo(c_message.as_ptr(), c_message.to_bytes().len());
    }
}