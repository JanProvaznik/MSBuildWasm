// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Wasmtime;

namespace MSBuildWasm
{
    public abstract class WasmTask : Microsoft.Build.Utilities.Task, IWasmTask
    {
        const string ExecuteFunctionName = "Execute";
        public string WasmFilePath { get; set; }
        public ITaskItem[] Directories { get; set; } = [new TaskItem(Directory.GetCurrentDirectory())];
        public bool InheritEnv { get; set; } = false;
        public string Environment { get; set; } = null;

        private static readonly HashSet<string> s_blacklistedProperties = [nameof(WasmFilePath), nameof(InheritEnv), nameof(ExecuteFunctionName)];
        private string _tempDir;
        private string _inputPath;
        private string _outputPath;

        public WasmTask()
        {
        }

        public override bool Execute()
        {
            CreateTempDir();
            CopyTaskItemsToTemp();
            CreateTaskIO();
            return ExecuteWasm();
        }
        private bool ExecuteWasm()
        {
            using var engine = new Engine();
            var wasiConfigBuilder = new WasiConfiguration();
            // setup env
            if (InheritEnv)
            {
                wasiConfigBuilder = wasiConfigBuilder.WithInheritedEnvironment();
            }

            // create a temporary directory for the task
            wasiConfigBuilder = wasiConfigBuilder.WithStandardOutput(_outputPath).WithStandardInput(_inputPath);
            wasiConfigBuilder = wasiConfigBuilder.WithPreopenedDirectory(_tempDir, ".");
            foreach (ITaskItem dir in Directories)
            {
                wasiConfigBuilder = wasiConfigBuilder.WithPreopenedDirectory(dir.ItemSpec, dir.ItemSpec);
            }
            // todo propagate info about directories to the task

            using var store = new Store(engine);
            store.SetWasiConfiguration(wasiConfigBuilder);


            using var module = Wasmtime.Module.FromFile(engine, WasmFilePath);
            using var linker = new Linker(engine);
            linker.DefineWasi();
            LinkLogFunctions(linker, store);
            LinkTaskInfo(linker, store);

            Instance instance = linker.Instantiate(store, module);
            Func<int> execute = instance.GetFunction<int>(ExecuteFunctionName);
            if (execute == null)
            {
                Log.LogError($"Function {ExecuteFunctionName} not found in WebAssembly module.");
                return false;
            }

            try
            {
                int taskResult = execute.Invoke();
                store.Dispose(); // store holds onto the output json file
                // read output file
                if (!ExtractOutputs())
                {
                    return false;
                }

                return taskResult == 0; // 0 is success inside, anything else is failure
            }
            catch (WasmtimeException ex)
            {
                Log.LogError($"Error executing WebAssembly module: {ex.Message}");
                return false;
            }
            catch (IOException ex)
            {
                Log.LogError($"Error reading output file: {ex.Message}");
                return false;

            }
            finally { Cleanup(); }


        }

        private void CreateTaskIO()
        {
            string taskInput = CreateTaskInputJSON();
            _inputPath = Path.Combine(_tempDir, "input.json");
            File.WriteAllText(_inputPath, taskInput);
            Log.LogMessage(MessageImportance.Low, $"Created input file: {_inputPath}");

            _outputPath = Path.Combine(_tempDir, "output.json");
        }
        private void CreateTempDir()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(_tempDir); // trycatch?
            Log.LogMessage(MessageImportance.Low, $"Created temporary directory: {_tempDir}");

        }

        private void Cleanup()
        {
            if (_tempDir != null)
            {
                Directory.Delete(_tempDir, true); // trycatch?
                Log.LogMessage(MessageImportance.Low, $"Removed temporary directory: {_tempDir}");
            }

        }
        private void CopyTaskItemsToTemp()
        {
            // reflect what ITaskItems and arrays are here in the class and copy them to the temp directory with appropriate paths
            // except directories
            PropertyInfo[] properties = GetType().GetProperties();

            foreach (PropertyInfo property in properties)
            {
                // Check if the property type is ITaskItem
                if (typeof(ITaskItem).IsAssignableFrom(property.PropertyType))
                {
                    // Get the value of the property (which should be an ITaskItem)
                    if (property.GetValue(this) is ITaskItem taskItem && taskItem != null)
                    {
                        // Get the ItemSpec (the path)
                        string sourcePath = taskItem.ItemSpec;
                        // Create the destination path in the _tmpPath directory
                        string destinationPath = Path.Combine(_tempDir, Path.GetFileName(sourcePath)); //sus

                        // Ensure the directory exists
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));

                        // Copy the file to the new location
                        File.Copy(sourcePath, destinationPath, overwrite: true);

                        Log.LogMessage(MessageImportance.Low, $"Copied {sourcePath} to {destinationPath}");

                    }
                }
            }
        }

        private static Dictionary<string, string> SerializeITaskItem(ITaskItem item)
        {
            var taskItemDict = new Dictionary<string, string>
            {
                ["ItemSpec"] = item.ItemSpec
            };
            foreach (object metadata in item.MetadataNames)
            {
                taskItemDict[metadata.ToString()] = item.GetMetadata(metadata.ToString());
            }
            return taskItemDict;
        }
        private static Dictionary<string, string>[] SerializeITaskItems(ITaskItem[] items)
        {
            return items.Select(SerializeITaskItem).ToArray();
        }

        private static bool IsSupportedType(Type type)
        {
            return type == typeof(string) || type == typeof(bool) || type == typeof(ITaskItem) || type == typeof(ITaskItem[]) || type == typeof(string[]) || type == typeof(bool[]);
        }

        /// <summary>
        /// Use reflection to gather properties of this class, except for the ones on the blacklist, and serialize them to a json.
        /// </summary>
        /// <returns>string of a json</returns>
        private string SerializeProperties()
        {
            var propertiesToSerialize = GetType()
                                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(p => !s_blacklistedProperties.Contains(p.Name) && IsSupportedType(p.PropertyType))
                                        .ToDictionary(p => p.Name, p =>
                                        {
                                            object value = p.GetValue(this);
                                            if (value is ITaskItem taskItem)
                                            {
                                                return SerializeITaskItem(taskItem);
                                            }
                                            else if (value is ITaskItem[] taskItemList)
                                            {
                                                return SerializeITaskItems(taskItemList);
                                            }
                                            return value;
                                        });

            return JsonSerializer.Serialize(propertiesToSerialize);
        }
        private string CreateTaskInputJSON()
        {
            var sb = new StringBuilder();
            sb.Append("{\"Properties\":");
            sb.Append(SerializeProperties());
            sb.Append('}');
            return sb.ToString();

        }


        /// <summary>
        /// 1. Read the output file
        /// 2. copy outputs that are files
        /// </summary>
        /// <returns></returns>
        private bool ExtractOutputs()
        {
            if (File.Exists(_outputPath))
            {
                string taskOutput = File.ReadAllText(_outputPath);
                // TODO: figure out where to copy the output files
                ReflectJson(taskOutput);
                return true;
            }
            else
            {
                Log.LogError("Output file not found");
                return false;
            }
        }

        /// <summary>
        ///  returns message importance according to its int value in the enum
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static MessageImportance ImportanceFromInt(int value)
        {
            return value switch
            {
                0 => MessageImportance.High,
                1 => MessageImportance.Normal,
                2 => MessageImportance.Low,
                _ => MessageImportance.Normal,
            };
        }
        /// <summary>
        /// Links logger functions to the WebAssembly module
        /// </summary>
        /// <param name="linker"></param>
        /// <param name="store"></param>
        /// <exception cref="Exception"></exception>
        protected void LinkLogFunctions(Linker linker, Store store)
        {
            linker.Define("msbuild-log", "LogMessage", Function.FromCallback(store, (Caller caller, int importance, int address, int length) =>
            {
                Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
                string message = memory.ReadString(address, length);
                Log.LogMessage(ImportanceFromInt(importance), message);
            }));

            linker.Define("msbuild-log", "LogError", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
                string message = memory.ReadString(address, length);
                Log.LogError(message);
            }));

            linker.Define("msbuild-log", "LogWarning", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
                string message = memory.ReadString(address, length);
                Log.LogWarning(message);
            }));
            Log.LogMessage(MessageImportance.Low, "Linked logger functions to WebAssembly module.");
        }

        /// <summary>
        /// Task Output JSON is in the form of a dictionary of output properties P.
        /// Read all keys of the JSON, values can be strings, dict<string,string> (representing ITaskItem), or bool, and lists of these things.
        /// Do Reflection for this class to obtain class properties R.
        /// for each property in P
        /// set R to value(P) if P==R
        /// </summary>
        /// <param name="taskOutput">Task Output Json</param>
        private void ReflectJson(string taskOutput)
        {
            try
            {
                // Parse the JSON string
                using JsonDocument document = JsonDocument.Parse(taskOutput);
                JsonElement root = document.RootElement;

                // Get all properties of the current class
                PropertyInfo[] properties = GetType().GetProperties();

                // Iterate through all properties in the JSON
                foreach (JsonProperty jsonProperty in root.EnumerateObject())
                {
                    // Find the matching property in the class
                    PropertyInfo classProperty = Array.Find(properties, p => p.Name.Equals(jsonProperty.Name, StringComparison.OrdinalIgnoreCase));

                    if (classProperty != null && classProperty.CanWrite)
                    {
                        // Set the property value based on its type
                        switch (jsonProperty.Value.ValueKind)
                        {
                            case JsonValueKind.String:
                                classProperty.SetValue(this, jsonProperty.Value.GetString());
                                break;
                            case JsonValueKind.Number:
                                throw new Exception("Numbers are not allowed as outputs of MSBuild tasks"); // todo exception type
                            case JsonValueKind.True:
                            case JsonValueKind.False:
                                classProperty.SetValue(this, jsonProperty.Value.GetBoolean());
                                break;
                            //case JsonValueKind.Array:
                            //    // Handle array types 
                            //    break;
                            case JsonValueKind.Object: // ITaskItem
                                // Copy from tmp to final location
                                string itemSpec = jsonProperty.Value.GetProperty("ItemSpec").GetString();
                                // TODO: copy metadata if they exist
                                File.Copy(Path.Combine(_tempDir, itemSpec), itemSpec, overwrite: true);
                                ITaskItem taskItem = new TaskItem(itemSpec);
                                classProperty.SetValue(this, taskItem);
                                break;
                            //case JsonValueKind.Array:
                            // TODO: Handle array types


                            // todo: ITaskItem and lists
                            default:
                                throw new Exception("Unexpected task output type."); // todo exception type
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                Log.LogError($"Error parsing JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error in ReflectJson: {ex.Message}");
            }
        }

        /// <summary>
        /// The task requires a function "TaskInfo" to be present in the WebAssembly module, it's used only in the factory to get the task properties.
        /// </summary>
        protected static void LinkTaskInfo(Linker linker, Store store) => linker.Define("msbuild-taskinfo", "TaskInfo", Function.FromCallback(store, (Caller caller, int address, int length) => { /* should do nothing in execution */ }));

    }
}
