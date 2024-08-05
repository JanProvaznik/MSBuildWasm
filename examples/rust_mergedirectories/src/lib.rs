mod msbuild;
use msbuild::logging::{log_warning, log_error};
use msbuild::task_info::{task_info, Property, PropertyType, TaskInfoStruct, TaskResult};
use serde::{Serialize, Deserialize};
use serde_json::Value;
use std::fs;
use std::io;
use std::path::Path;

/// Entry point for the task. It receives input properties from stdin and writes output properties to stdout
/// input should be in the form of a JSON {properties:{"name":"value", ...}}. 
/// output should be in the form of a JSON {properties:{"name":"value", ...}}.
#[no_mangle] #[allow(non_snake_case)]
pub fn Execute() -> TaskResult {
    // Task input properties in stdin
    let mut input = String::new();
    std::io::stdin().read_line(&mut input).unwrap();
    
    // Deserialize task input
    let task_input: TaskInput = match serde_json::from_str(&input) {
        Ok(input) => input,
        Err(_) => {
            log_error("Failed to parse task input.");
            return TaskResult::Failure;
        }
    };

    let wasm_paths: Vec<String> = task_input
        .properties
        .Dirs
        .iter()
        .filter_map(|dir| dir.get("WasmPath"))
        .filter_map(|wasm_path| wasm_path.as_str())
        .map(|s| s.to_string())
        .collect();

    let output_name = task_input.properties.MergedName.unwrap_or("output_dir".to_string());
    
    if let Err(err) = merge_directories(&wasm_paths, &output_name) {
        log_error(&format!("Failed to merge directories: {}", err));
        return TaskResult::Failure;
    }

    for path in wasm_paths {
        log_warning(&format!("WasmPath: {}", path));
    }

    let output_struct = OutputStruct {
        properties: OutputProperties {
            MergedDir: MergedDirStruct {
                ItemSpec: output_name.to_string(),
                WasmPath: output_name.to_string(),
            },
        }
    };

    println!("{}", serde_json::to_string(&output_struct).unwrap());

    TaskResult::Success
}

/// Rust wasm task implementation exports GetTaskInfo which is called by the host environment to get the task info
/// use the TaskInfoStruct to define the task's properties.
#[no_mangle] #[allow(non_snake_case)]
pub fn GetTaskInfo() {
    let task_info_struct = TaskInfoStruct {
        name: String::from("MergeDirectoriesTask"),
        properties: vec![
            Property {
                name: String::from("Dirs"),
                output: false,
                required: true,
                property_type: PropertyType::ITaskItemArray,
            },
            Property {
                name: String::from("MergedName"),
                output: false,
                required: false,
                property_type: PropertyType::String,
            },
            Property {
                name: String::from("MergedDir"),
                output: true,
                required: false,
                property_type: PropertyType::ITaskItem,
            },
        ],
    };
    task_info(task_info_struct);
}

#[derive(Debug, Serialize, Deserialize)] #[allow(non_snake_case)]
struct MergedDirStruct {
    ItemSpec: String,
    WasmPath: String,
}
#[derive(Debug, Serialize, Deserialize)] #[allow(non_snake_case)]
struct OutputProperties {
    MergedDir: MergedDirStruct,
}

#[derive(Debug, Serialize, Deserialize)]
struct OutputStruct {
    properties: OutputProperties,
}

#[derive(Debug, Deserialize)] #[allow(non_snake_case)]
struct Properties {
    Dirs: Vec<Value>,
    MergedName: Option<String>,
}

#[derive(Debug, Deserialize)]
struct TaskInput {
    properties: Properties,
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