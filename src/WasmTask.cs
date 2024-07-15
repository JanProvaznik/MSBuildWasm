// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Wasmtime;

namespace MSBuildWasm
{
    public abstract class WasmTask : Microsoft.Build.Utilities.Task, IWasmTask
    {
        const string ExecuteFunctionName = "Execute";
        public string WasmFilePath { get; set; }
        public ITaskItem[] Directories { get; set; }
        public bool InheritEnv { get; set; } = false;
        public string Environment { get; set; } = null;

        private static readonly HashSet<string> s_blacklistedProperties = new HashSet<string> { nameof(WasmFilePath), nameof(InheritEnv), nameof(ExecuteFunctionName) };
        private string _tempDir;
        private string _inputPath;
        private string _outputPath;

        public WasmTask()
        {
        }

        public override bool Execute()
        {
            CreateTempDir();
            CreateTaskIO();
            return ExecuteWasm();
        }
        private bool ExecuteWasm()
        {
            using var engine = new Engine();
            var wasiConfigBuilder = new WasiConfiguration();
            if (InheritEnv)
            {
                wasiConfigBuilder = wasiConfigBuilder.WithInheritedEnvironment();
            }

            // create a temporary directory for the task
            wasiConfigBuilder = wasiConfigBuilder.WithStandardOutput(_outputPath).WithStandardInput(_inputPath);

            using var store = new Store(engine);
            store.SetWasiConfiguration(wasiConfigBuilder);


            using var module = Wasmtime.Module.FromFile(engine, WasmFilePath);
            using var linker = new Linker(engine);
            linker.DefineWasi();
            LinkLogFunctions(linker, store);
            LinkTaskInfo(linker, store);

            Instance instance = linker.Instantiate(store, module);
            var execute = instance.GetFunction<int>(ExecuteFunctionName);
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
            var taskInput = SerializeProperties();
            _inputPath = Path.Combine(_tempDir, "input.json");
            File.WriteAllText(_inputPath, taskInput);
            Log.LogMessage(MessageImportance.Low, $"Created input file: {_inputPath}");

            _outputPath = Path.Combine(_tempDir, "output.json");
        }
        /// <summary>
        /// 
        /// </summary>
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



        /// <summary>
        /// Use reflection to gather properties of this class, except for the ones on the blacklist, and serialize them to a json.
        /// TODO: ITaskItem and lists
        /// </summary>
        /// <returns>string of a json</returns>
        private string SerializeProperties()
        {

            var propertiesToSerialize = GetType()
                                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(p => !s_blacklistedProperties.Contains(p.Name) && (p.PropertyType == typeof(string) || p.PropertyType == typeof(bool)))
                                        .ToDictionary(p => p.Name, p => p.GetValue(this));

            return JsonSerializer.Serialize(propertiesToSerialize);
        }

        private bool ExtractOutputs()
        {
            if (File.Exists(_outputPath))
            {
                string taskOutput = File.ReadAllText(_outputPath);
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
            switch (value)
            {
                case 0: return MessageImportance.High;
                case 1: return MessageImportance.Normal;
                case 2: return MessageImportance.Low;
                default: return MessageImportance.Normal;
            }
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
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new WasmtimeException("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
                Log.LogMessage(ImportanceFromInt(importance), message);
            }));

            linker.Define("msbuild-log", "LogError", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new WasmtimeException("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
                Log.LogError(message);
            }));

            linker.Define("msbuild-log", "LogWarning", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new WasmtimeException("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
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
        internal void ReflectJson(string taskOutput)
        {
            try
            {
                // Parse the JSON string
                using JsonDocument document = JsonDocument.Parse(taskOutput);
                JsonElement root = document.RootElement;

                // Get all properties of the current class
                PropertyInfo[] properties = this.GetType().GetProperties();

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
                            //case JsonValueKind.Object:
                            //    // Handle nested objects 
                            //    break;
                            //    // Add more cases as needed
                            // todo: ITaskItem and lists
                            default:
                                throw new Exception("Unexpected task output type."); // todo exception type
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                // Log the error
                Log.LogError($"Error parsing JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Log any other errors
                Log.LogError($"Error in ReflectJson: {ex.Message}");
            }
        }

        /// <summary>
        /// The task requires a function "TaskInfo" to be present in the WebAssembly module, it's used only in the factory to get the task properties.
        /// </summary>
        protected void LinkTaskInfo(Linker linker, Store store)
        {
            linker.Define("msbuild-taskinfo", "TaskInfo", Function.FromCallback(store, (Caller caller, int address, int length) => { /* should do nothing in execution */ }));
        }

    }
}
