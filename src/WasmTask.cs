// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Framework;
using Wasmtime;


namespace MSBuildWasm
{
    /// <summary>
    /// Base class for WebAssembly tasks for MSBuild.
    /// It appears to MSBuild as a regular task.
    /// 1. a class deriving from in is created in WasmTaskFactory during runtime, Properties are added to it using reflection.
    /// 2. MSBuild sets the values of properties of an instance of the class from .proj XML
    /// 3. Execute is called from MSBuild and runs the WebAssembly module with input created from properties of the class
    /// </summary>
    public abstract class WasmTask : Microsoft.Build.Utilities.Task
    {
        /// <summary>
        /// The name of the function to execute in the WebAssembly module.
        /// </summary>
        public const string ExecuteFunctionName = "Execute";
        /// <summary>
        /// The name of the function to get task info from the WebAssembly module.
        /// </summary>
        public const string GetTaskInfoFunctionName = "GetTaskInfo";
        /// <summary>
        /// The path to the WebAssembly module.
        /// </summary>
        public string WasmFilePath { get; set; }
        /// <summary>
        /// Preopened directories for the WebAssembly module.
        /// </summary>
        public ITaskItem[] Directories { get; set; } = [];
        /// <summary>
        /// Whether to inherit the environment when running the WebAssembly module.
        /// </summary>
        public bool InheritEnv { get; set; } = false;

        // Reflection in WasmTaskFactory will add properties to a subclass.


        /// <summary>
        /// We don't want to serialize excluded properties using reflection to give them to the WebAssembly module.
        /// </summary>
        internal readonly HashSet<string> _excludedPropertyNames =
            [nameof(WasmFilePath), nameof(InheritEnv), nameof(ExecuteFunctionName), nameof(GetTaskInfoFunctionName),
            nameof(Directories), ];

        private FileIsolator _fileIsolator;

        public WasmTask()
        {
        }

        public void Initialize()
        {
            _fileIsolator = new FileIsolator(Log);
            CopyAllTaskItemPropertiesToTmp();

        }
        /// <summary>
        /// Executes the WebAssembly task.
        /// </summary>
        /// <returns>True if the task executed successfully, false otherwise.</returns>
        public override bool Execute()
        {
            Initialize();
            CreateTaskIO();
            ExecuteWasm();
            return !Log.HasLoggedErrors;
        }
        /// <summary>
        /// Creates a WasiConfiguration for the Wasmtime from properties of this class.
        /// </summary>
        /// <returns>WasiConfiguration with appropriately pre-opened directories and stdio.</returns>
        private WasiConfiguration CreateWasiConfig()
        {
            var wasiConfig = new WasiConfiguration();
            if (InheritEnv)
            {
                wasiConfig = wasiConfig.WithInheritedEnvironment();
            }
            // Stdin is used to pass property values to the task, stdout is used to pass output property values from the task
            wasiConfig = wasiConfig.WithStandardOutput(_fileIsolator._outputPath).WithStandardInput(_fileIsolator._inputPath);
            wasiConfig = wasiConfig.WithPreopenedDirectory(_fileIsolator._sharedTmpDir.FullName, ".");
            foreach (ITaskItem dir in Directories)
            {
                wasiConfig = wasiConfig.WithPreopenedDirectory(dir.ItemSpec, dir.ItemSpec);
            }

            return wasiConfig;

        }
        /// <summary>
        /// Sets up the Wasmtime instance for executing the WebAssembly module.
        /// </summary>
        /// <param name="engine">The Wasmtime engine.</param>
        /// <param name="store">The Wasmtime store.</param>
        /// <returns>An Instance object representing the instantiated WebAssembly module.</returns>
        private Instance SetupWasmtimeInstance(Engine engine, Store store)
        {
            store.SetWasiConfiguration(CreateWasiConfig());

            using var module = Wasmtime.Module.FromFile(engine, WasmFilePath);
            using var linker = new WasmTaskLinker(engine, Log);
            linker.DefineWasi();
            linker.LinkLogFunctions(store);
            linker.LinkTaskInfo(store, null);

            return linker.Instantiate(store, module);

        }
        /// <summary>
        /// Executes the WebAssembly module.
        /// </summary>
        private void ExecuteWasm()
        {
            using var engine = new Engine();
            using var store = new Store(engine);
            Instance instance = SetupWasmtimeInstance(engine, store);

            Func<int> execute = instance.GetFunction<int>(ExecuteFunctionName);

            try
            {
                if (execute == null)
                {
                    throw new WasmtimeException($"Function {ExecuteFunctionName} not found in WebAssembly module.");
                }

                int taskResult = execute.Invoke();
                store.Dispose(); // store holds onto the output json file
                ExtractOutputs();
                if (taskResult != 0)
                {
                    Log.LogError($"Task failed.");
                }
            }
            catch (WasmtimeException ex)
            {
                Log.LogError($"Error executing WebAssembly module: {ex.Message}");
            }
            catch (IOException ex)
            {
                Log.LogError($"Error reading output file: {ex.Message}");

            }
            finally { _fileIsolator.Cleanup(); }

        }

        private void CreateTaskIO()
        {
            File.WriteAllText(_fileIsolator._inputPath, Serializer.SerializeTaskInput(this));
            Log.LogMessage(MessageImportance.Low, $"Created input file: {_fileIsolator._inputPath}");
        }

        /// <summary>
        /// Use reflection to figure out what ITaskItem and ITaskItem[] typed properties are here in the class.
        /// Copy their content to the tmp directory for the sandbox to use.
        /// TODO: check size to not copy large amounts of data
        /// </summary>
        /// <remarks>If wasmtime implemented per-file access this effort could be saved.</remarks>
        /// TODO: save effort copying if a file's Directory is preopened
        private void CopyAllTaskItemPropertiesToTmp()
        {
            IEnumerable<PropertyInfo> properties = GetType().GetProperties().Where(p =>
                 (typeof(ITaskItem).IsAssignableFrom(p.PropertyType) || typeof(ITaskItem[]).IsAssignableFrom(p.PropertyType))
                 && !_excludedPropertyNames.Contains(p.Name)
             );

            foreach (PropertyInfo property in properties)
            {
                CopyPropertyToTmp(property);
            }
        }

        /// <summary>
        /// Copies a single property to the temporary directory.
        /// </summary>
        /// <param name="property">The PropertyInfo of the property to copy.</param>
        private void CopyPropertyToTmp(PropertyInfo property)
        {
            object value = property.GetValue(this);

            if (value is ITaskItem taskItem)
            {
                _fileIsolator.CopyTaskItemToSandbox(taskItem);
            }
            else if (value is ITaskItem[] taskItems)
            {
                foreach (ITaskItem taskItem_ in taskItems)
                {
                    _fileIsolator.CopyTaskItemToSandbox(taskItem_);
                }
            }
        }

        /// <summary>
        /// Extracts outputs from the WebAssembly task execution.
        /// </summary>
        /// <returns>True if outputs were successfully extracted, false otherwise.</returns>
        private bool ExtractOutputs()
        {

            if (_fileIsolator.TryGetTaskOutput(out string taskOutput))
            {
                ReflectOutputJsonToClassProperties(taskOutput);
                return true;
            }
            else
            {
                Log.LogError("Output file not found");
                return false;
            }

        }

        /// <summary>
        /// Reflects a JSON property to a class property.
        /// </summary>
        /// <param name="classProperties">Array of PropertyInfo for the class properties.</param>
        /// <param name="jsonProperty">The JSON property to reflect.</param>
        private void ReflectJsonPropertyToClassProperty(PropertyInfo[] classProperties, JsonProperty jsonProperty)
        {
            // Find the matching property in the class
            PropertyInfo classProperty = Array.Find(classProperties, p => p.Name.Equals(jsonProperty.Name, StringComparison.OrdinalIgnoreCase));

            if (classProperty == null || !classProperty.CanWrite)
            {
                Log.LogMessage(MessageImportance.Normal, $"Property outputted by WasmTask {jsonProperty.Name} not found or is read-only.");
                return;
            }
            // if property is not output don't copy
            if (classProperty.GetCustomAttribute<OutputAttribute>() == null)
            {
                Log.LogMessage(MessageImportance.Normal, $"Property outputted by WasmTask {jsonProperty.Name} does not have the Output attribute, ignoring.");
                return;
            }

            // Parse and set the property value based on its type
            classProperty.SetValue(this, classProperty.PropertyType switch
            {
                Type t when t == typeof(string) => jsonProperty.Value.GetString(),
                Type t when t == typeof(bool) => jsonProperty.Value.GetBoolean(),
                Type t when t == typeof(ITaskItem) => ExtractTaskItem(jsonProperty.Value),
                Type t when t == typeof(ITaskItem[]) => ExtractTaskItems(jsonProperty.Value),
                Type t when t == typeof(string[]) => jsonProperty.Value.EnumerateArray().Select(j => j.GetString()).ToArray(),
                Type t when t == typeof(bool[]) => jsonProperty.Value.EnumerateArray().Select(j => j.GetBoolean()).ToArray(),
                _ => throw new ArgumentException($"Unsupported property type: {classProperty.PropertyType}")
            });
        }

        /// <summary>
        /// Task Output JSON is in the form of a dictionary of output properties P.
        /// Read all keys of the JSON, values can be strings, dict<string,string> (representing ITaskItem), or bool, and lists of these things.
        /// Do Reflection for this class to obtain class properties R.
        /// for each property in P
        /// set R to value(P) if P==R
        /// </summary>
        /// <param name="taskOutputJson">Task Output JSON</param>
        private void ReflectOutputJsonToClassProperties(string taskOutputJson)
        {
            try
            {
                // Get all properties of the current class
                PropertyInfo[] classProperties = GetType().GetProperties();

                foreach (JsonProperty jsonProperty in Serializer.JsonPropertiesEnumeration(taskOutputJson))
                {
                    ReflectJsonPropertyToClassProperty(classProperties, jsonProperty);
                }
            }
            catch (JsonException ex)
            {
                Log.LogError($"Error parsing JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error Reflecting properties from Json to Class: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies file/directory from sandboxing path based on information from the JSON element.
        /// </summary>
        /// <param name="jsonElement">The JSON element containing the task item data.</param>
        /// <returns>An ITaskItem representing the extracted task item.</returns>
        private ITaskItem ExtractTaskItem(JsonElement jsonElement)
        {
            string wasmPath = Serializer.GetGuestPath(jsonElement);
            string itemSpec = Serializer.GetHostPath(jsonElement);
            return _fileIsolator.CopyGuestToHost(wasmPath, itemSpec);
        }

        /// <summary>
        /// Extracts multiple task items from a JSON element.
        /// </summary>
        /// <param name="jsonElement">The JSON element containing the task items data.</param>
        /// <returns>An array of ITaskItem representing the extracted task items.</returns>
        private ITaskItem[] ExtractTaskItems(JsonElement jsonElement)
        {
            return jsonElement.EnumerateArray().Select(ExtractTaskItem).ToArray();
        }
    }
}
