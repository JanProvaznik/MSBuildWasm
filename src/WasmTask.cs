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
    public class WasmTask : Microsoft.Build.Utilities.Task, IWasmTask
    {
        const string ExecuteFunctionName = "execute";
        public string WasmFilePath { get; set; }
        private static readonly HashSet<string> BlacklistedProperties = new HashSet<string> { "WasmFilePath" };

        //public string MyProperty { get; set; }


        public WasmTask()
        {
        }


        public override bool Execute()
        {
            Debugger.Launch();
            return ExecuteWasm();
        }
        private bool ExecuteWasm()
        {
            using var engine = new Engine();
            var wasiConfigBuilder = new WasiConfiguration();

            //if (InheritEnv)
            //{
            //    wasiConfigBuilder = wasiConfigBuilder.WithInheritedEnvironment();
            //}
            //string tmpPath = "tmp"; // TBD
            //if (EnableTmp)
            //{
            //    var dir = Directory.CreateDirectory(tmpPath);
            //    wasiConfigBuilder = wasiConfigBuilder.WithPreopenedDirectory(dir.FullName, ".");
            //}
            //if (HomeDir != null)
            //{
            //    var dir = Directory.CreateDirectory(outDirName);
            //    wasiConfigBuilder = wasiConfigBuilder.WithPreopenedDirectory(dir.FullName, "/out");
            //}
            //if (EnableIO)
            //{
            //    wasiConfigBuilder = wasiConfigBuilder.WithStandardOutput(outputPath)
            //                                         .WithStandardError(errorPath);
            //}
            wasiConfigBuilder.WithEnvironmentVariable("MSBuildInput", SerializeProperties());

            using var store = new Store(engine);
            store.SetWasiConfiguration(wasiConfigBuilder);


            using var module = Wasmtime.Module.FromFile(engine, WasmFilePath);
            using var linker = new Linker(engine);
            linker.DefineWasi();
            LinkLogFunctions(linker, store);
            LinkTaskInfo(linker, store);
            LinkOutputGathering(linker, store);

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
                return taskResult == 0; // 0 is success inside, anything else is failure
            }
            catch (WasmtimeException)
            {
                return false;
            }

        }



        /// <summary>
        /// Use reflection to gather properties of this class, except for the ones on blacklist, and put them in a json, they can be bools or strings
        /// </summary>
        /// <returns>string of a json of that</returns>
        private string SerializeProperties()
        {

            var propertiesToSerialize = GetType()
    .GetProperties(BindingFlags.Public | BindingFlags.Instance)
    .Where(p => !BlacklistedProperties.Contains(p.Name) &&
                (p.PropertyType == typeof(string) || p.PropertyType == typeof(bool)))
    .ToDictionary(p => p.Name, p => p.GetValue(this));

            return JsonSerializer.Serialize(propertiesToSerialize);


        }
        private bool ExtractOutputs()
        {
            return true;
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
                    throw new Exception("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
                Log.LogMessage(ImportanceFromInt(importance), message);
            }));

            linker.Define("msbuild-log", "LogError", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new Exception("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
                Log.LogError(message);
            }));

            linker.Define("msbuild-log", "LogWarning", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new Exception("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
                Log.LogWarning(message);
            }));

        }

        /// <summary>
        /// Link a function that gives the host a string representation of a json of the outputs
        /// </summary>
        /// <param name="linker"></param>
        /// <param name="store"></param>
        /// <exception cref="Exception"></exception>
        protected void LinkOutputGathering(Linker linker, Store store)
        {
            linker.Define("msbuild-output", "Output", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new Exception("WebAssembly module did not export a memory.");
                }

                var output = memory.ReadString(address, length);
                ReflectJson(output);

            }));
        }
        protected void LinkTaskInfo(Linker linker, Store store)
        {
            linker.Define("msbuild-taskinfo", "TaskInfo", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                // nothing in execution
            }));

        }


        //private void ReflectJson(string taskOutput)
        ////{
        //    // the json is in the form of a dictionary of output properties P
        //    // Read all keys of the json, values can be strings, dict<string,string>, or bool, and lists of these things
        //    // implement only string variant for now

        //    // do Reflection for this class to obtain class properties R
        //    // foreach prop in P
        //    // set R to value(P) if P=R


        //}

        private void ReflectJson(string taskOutput)
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
                            //    // Handle array types (you may need to implement this based on your specific needs)
                            //    break;
                            //case JsonValueKind.Object:
                            //    // Handle nested objects (you may need to implement this based on your specific needs)
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


        protected void LinkInputGathering()
        {

        }

        private void JsonToProperties(string json)
        {

        }


    }
}
