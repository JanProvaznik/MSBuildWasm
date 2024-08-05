mod msbuild;
use msbuild::logging::{log_warning};
use msbuild::task_info::{task_info, Property, PropertyType, TaskInfoStruct, TaskResult};
use serde::{Serialize, Deserialize};


/// Entry point for the task. It receives input properties from stdin and writes output properties to stdout
/// input should be in the form of a JSON {properties:{"name":"value", ...}}. 
/// output should be in the form of a JSON {properties:{"name":"value", ...}}.
#[no_mangle] #[allow(non_snake_case)]
pub fn Execute() -> TaskResult
{
        // Task input properties in stdin
        let mut input = String::new();
        std::io::stdin().read_line(&mut input).unwrap();
        
        // check msbuild::logging provides logging messages, warnings and errors
        // show what the template task got on input
        log_warning(&input);
        

        // println!("{}",r#"{"TestOutputProperty":"This is the output property value from WASM task"}"#);
        // with serde
        let output_struct = OutputStruct {
            properties: OutputProperties {
                TestOutputProperty: String::from("This is the output property value from WASM task"),
            },
        };
        println!("{}", serde_json::to_string(&output_struct).unwrap());


        return TaskResult::Success;
}

/// Rust wasm task implementation exports GetTaskInfo which is called by the host environment to get the task info
/// use the TaskInfoStruct to define the task's properties.
#[no_mangle] #[allow(non_snake_case)]
pub fn GetTaskInfo() 
{
    let task_info_struct = TaskInfoStruct {
        name: String::from("template"),
        properties: vec![
            Property {
                name: String::from("TestNormalProperty"),
                output: false,
                required: false,
                property_type: PropertyType::String,
            },
            Property {
                name: String::from("TestOutputProperty"),
                output: true,
                required: false,
                property_type: PropertyType::String,
            },
            Property {
                name: String::from("TestRequiredProperty"),
                output: false,
                required: true,
                property_type: PropertyType::String,
            },
            Property {
                name: String::from("TestBoolProperty"),
                output: false,
                required: false,
                property_type: PropertyType::Bool,
            },
        ],
    };
    task_info(task_info_struct);
}

/// Example Output Struct with Properties,
/// implement according to your task's output properties,
/// if a property is of type `ITaskItem`` it should be a dictionary
#[derive(Debug, Serialize, Deserialize)] #[allow(non_snake_case)]
struct OutputProperties {
    TestOutputProperty: String,
}

#[derive(Debug, Serialize, Deserialize)]
struct OutputStruct {
    properties: OutputProperties,
}