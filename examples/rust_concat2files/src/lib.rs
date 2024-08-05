mod msbuild;
// use msbuild::logging::{log_warning, log_error, log_message};
use msbuild::task_info::{task_info, Property, PropertyType, TaskInfoStruct, TaskResult};
use serde::{Serialize, Deserialize};

/// Entry point for the task. It receives input properties from stdin and writes output properties to stdout.
/// Input should be in the form of a JSON {properties:{"name":"value", ...}}. 
/// Output should be in the form of a JSON {properties:{"name":"value", ...}}.
#[no_mangle] #[allow(non_snake_case)]
pub fn Execute() -> TaskResult
{
    // Task input properties in stdin
    let mut input = String::new();
    std::io::stdin().read_line(&mut input).unwrap();

    // Deserialize the input JSON to access properties
    let input_json: serde_json::Value = serde_json::from_str(&input).unwrap();
    // Extract the input file paths from the JSON
    let input_file1 = input_json["properties"]["InputFile1"]["WasmPath"].as_str().unwrap();
    let input_file2 = input_json["properties"]["InputFile2"]["WasmPath"].as_str().unwrap();
    // Extract the output file path from the JSON but if it does not exist then use a default value wasmconcatoutput.txt
    let output_file = input_json["properties"]["OutputName"].as_str().unwrap_or("wasmconcatoutput.txt");

    // Read the contents of the input files
    let input_file1_contents = std::fs::read_to_string(input_file1).unwrap();
    let input_file2_contents = std::fs::read_to_string(input_file2).unwrap();

    // Concatenate the contents
    let output_contents = format!("{}{}", input_file1_contents, input_file2_contents);

    // Write to output file
    std::fs::write(output_file, output_contents).unwrap();

    // Prepare output properties and log the result
    let output_properties = OutputProperties {
        OutputFile: OutputFile {
            ItemSpec: output_file.to_string(),
            WasmPath: output_file.to_string(),
        },
    };

    let output_struct = OutputStruct {
        properties: output_properties,
    };

    // Print the output properties as JSON
    println!("{}", serde_json::to_string(&output_struct).unwrap());

    TaskResult::Success
}

/// Rust wasm task implementation exports GetTaskInfo which is called by the host environment to get the task info.
/// Use the TaskInfoStruct to define the task's properties.
#[no_mangle] #[allow(non_snake_case)]
pub fn GetTaskInfo() 
{
    let task_info_struct = TaskInfoStruct {
        name: String::from("concat2files"),
        properties: vec![
            Property {
                name: String::from("InputFile1"),
                output: false,
                required: true,
                property_type: PropertyType::ITaskItem,
            },
            Property {
                name: String::from("InputFile2"),
                output: false,
                required: true,
                property_type: PropertyType::ITaskItem,
            },
            Property {
                name: String::from("OutputName"),
                output: false,
                required: false,
                property_type: PropertyType::String,
            },
            Property {
                name: String::from("OutputFile"),
                output: true,
                required: false,
                property_type: PropertyType::ITaskItem,
            },
        ],
    };
    task_info(task_info_struct);
}

#[derive(Debug, Serialize, Deserialize)] #[allow(non_snake_case)]
struct OutputFile {
    ItemSpec: String,
    WasmPath: String,
}

#[derive(Debug, Serialize, Deserialize)] #[allow(non_snake_case)]
struct OutputProperties {
    OutputFile: OutputFile,
}

#[derive(Debug, Serialize, Deserialize)]
struct OutputStruct {
    properties: OutputProperties,
}
