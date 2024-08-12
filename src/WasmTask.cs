// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
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
    public abstract class WasmTask : Microsoft.Build.Utilities.Task, IWasmTask
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

            if (value is ITaskItem taskItem && taskItem != null)
            {
                _fileIsolator.CopyTaskItemToTmpDir(taskItem);
            }
            else if (value is ITaskItem[] taskItems && taskItems != null)
            {
                foreach (ITaskItem taskItem_ in taskItems)
                {
                    _fileIsolator.CopyTaskItemToTmpDir(taskItem_);
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
                Log.LogMessage(MessageImportance.Normal, $"Property outupted by WasmTask {jsonProperty.Name} not found or is read-only.");
                return;
            }
            // if property is not output don't copy
            if (classProperty.GetCustomAttribute<OutputAttribute>() == null)
            {
                Log.LogMessage(MessageImportance.Normal, $"Property outupted by WasmTask {jsonProperty.Name} does not have the Output attribute, ignoring.");
                return;
            }

            // Parse and set the property value based on its type
            // note: can't use a switch because Type is not a constant
            if (classProperty.PropertyType == typeof(string))
            {
                classProperty.SetValue(this, jsonProperty.Value.GetString());
            }
            else if (classProperty.PropertyType == typeof(bool))
            {
                classProperty.SetValue(this, jsonProperty.Value.GetBoolean());
            }
            else if (classProperty.PropertyType == typeof(ITaskItem))
            {
                classProperty.SetValue(this, ExtractTaskItem(jsonProperty.Value));
            }
            else if (classProperty.PropertyType == typeof(ITaskItem[]))
            {
                classProperty.SetValue(this, ExtractTaskItems(jsonProperty.Value));
            }
            else if (classProperty.PropertyType == typeof(string[]))
            {
                classProperty.SetValue(this, jsonProperty.Value.EnumerateArray().Select(j => j.GetString()).ToArray());
            }
            else if (classProperty.PropertyType == typeof(bool[]))
            {
                classProperty.SetValue(this, jsonProperty.Value.EnumerateArray().Select(j => j.GetBoolean()).ToArray());
            }
            else
            {
                throw new ArgumentException($"Unsupported property type: {classProperty.PropertyType}"); // this should never happen, only programming error in this class or factory could cause it.
            }

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
                using JsonDocument document = JsonDocument.Parse(taskOutputJson);
                JsonElement root = document.RootElement;

                // Get all properties of the current class
                PropertyInfo[] classProperties = GetType().GetProperties();

                // Iterate through all properties in the JSON
                JsonElement properties = root.GetProperty("properties");
                foreach (JsonProperty jsonProperty in properties.EnumerateObject())
                {
                    ReflectJsonPropertyToClassProperty(classProperties, jsonProperty);
                }
            }
            catch (JsonException ex)
            {
                Log.LogError($"Error parsing output JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Error Reflecting properties from Json to Class after task run: {ex.Message}");
            }
        }

        /// <summary>
        /// Copies file/directory from sandboxing path based on information from the JSON element.
        /// </summary>
        /// <param name="jsonElement">The JSON element containing the task item data.</param>
        /// <returns>An ITaskItem representing the extracted task item.</returns>
        private ITaskItem ExtractTaskItem(JsonElement jsonElement)
        {
            // guest internal path
            string wasmPath = jsonElement.GetProperty("WasmPath").GetString();
            // host path
            string itemSpec = jsonElement.GetProperty("ItemSpec").GetString(); // TODO: firgure out if this is bad for security
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

    /// <summary>
    /// Helper class for isolating files.
    /// </summary>
    internal class FileIsolator
    {
        private readonly TaskLoggingHelper _log;

        internal DirectoryInfo _sharedTmpDir { get; set; }
        internal DirectoryInfo _hostTmpDir { get; set; }
        internal string _inputPath { get; set; }
        internal string _outputPath { get; set; }


        public FileIsolator(TaskLoggingHelper log)
        {
            _log = log;
            CreateTmpDirs();
            _inputPath = Path.Combine(_hostTmpDir.FullName, "input.json");
            _outputPath = Path.Combine(_hostTmpDir.FullName, "output.json");
        }

        /// <summary>
        /// Copies a file or directory from the guest (sandbox) environment to the host.
        /// </summary>
        /// <param name="wasmPath">The path in the sandbox environment.</param>
        /// <param name="itemSpec">The destination path on the host.</param>
        /// <returns>A TaskItem representing the copied item, or null if the item was not found.</returns>

        internal TaskItem CopyGuestToHost(string wasmPath, string itemSpec)
        {
            string sandboxOuterPath = Path.Combine(_sharedTmpDir.FullName, wasmPath);

            if (File.Exists(sandboxOuterPath))
            {
                File.Copy(sandboxOuterPath, itemSpec, overwrite: true);
            }
            else if (Directory.Exists(sandboxOuterPath))
            {
                DirectoryCopy(sandboxOuterPath, itemSpec);
            }
            else
            {
                // nothing to copy
                _log.LogMessage(MessageImportance.Normal, $"Task output not found");
                return null;
            }
            return new TaskItem(itemSpec);

        }
        /// <summary>
        /// Creates directories for sandboxing.
        /// </summary>
        internal void CreateTmpDirs()
        {
            _sharedTmpDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
            _hostTmpDir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()));
            _log.LogMessage(MessageImportance.Low, $"Created shared temporary directories: {_sharedTmpDir} and {_hostTmpDir}");
        }

        /// <summary>
        /// Removes temporary directories.
        /// </summary>
        internal void Cleanup()
        {
            DeleteTemporaryDirectory(_sharedTmpDir);
            DeleteTemporaryDirectory(_hostTmpDir);
        }

        /// <summary>
        /// Helper function for deleting a directory.
        /// </summary>
        /// <param name="directory"></param>
        private void DeleteTemporaryDirectory(DirectoryInfo directory)
        {
            if (directory != null)
            {
                try
                {
                    Directory.Delete(directory.FullName, true);
                    _log.LogMessage(MessageImportance.Low, $"Removed temporary directory: {directory}");
                }
                catch (Exception ex)
                {
                    _log.LogMessage(MessageImportance.High, $"Failed to remove temporary directory: {directory}. Exception: {ex.Message}");
                }
            }
        }
        private static void DirectoryCopy(string sourcePath, string destinationPath)
        {
            DirectoryInfo diSource = new DirectoryInfo(sourcePath);
            DirectoryInfo diTarget = new DirectoryInfo(destinationPath);

            CopyAll(diSource, diTarget);
        }
        private static void CopyAll(DirectoryInfo source, DirectoryInfo target)
        {
            Directory.CreateDirectory(target.FullName);

            // Copy each file into the new directory.
            foreach (FileInfo fi in source.GetFiles())
            {
                fi.CopyTo(Path.Combine(target.FullName, fi.Name), true);
            }

            // Copy each subdirectory using recursion.
            foreach (DirectoryInfo diSourceSubDir in source.GetDirectories())
            {
                DirectoryInfo nextTargetSubDir =
                    target.CreateSubdirectory(diSourceSubDir.Name);
                CopyAll(diSourceSubDir, nextTargetSubDir);
            }
        }
        /// <summary>
        /// We put all files to the sandbox to the root.
        /// </summary>
        /// <param name="path">original path</param>
        /// <returns>flattened sandbox path</returns>
        private string ConvertToSandboxPath(string path)
        {
            return path.Replace(Path.DirectorySeparatorChar, '_')
                       .Replace(Path.AltDirectorySeparatorChar, '_')
                       .Replace(':', '_');
        }
        /// <summary>
        /// Tries to read the task output from the output file.
        /// </summary>
        /// <param name="taskOutput">output file contents</param>
        /// <returns>true if the output file exists</returns>
        internal bool TryGetTaskOutput(out string taskOutput)
        {
            if (File.Exists(_outputPath))
            {
                taskOutput = File.ReadAllText(_outputPath);
                return true;
            }
            else
            {
                taskOutput = null;
                return false;
            }
        }
        /// <summary>
        /// Copies file/directory to sandbox directory.
        /// </summary>
        /// <param name="taskItem">TaskItem representation of a file/directory</param>
        internal void CopyTaskItemToTmpDir(ITaskItem taskItem)
        {
            // ItemSpec = path in usual circumstances
            string sourcePath = taskItem.ItemSpec;
            string sandboxPath = ConvertToSandboxPath(sourcePath);
            string destinationPath = Path.Combine(_sharedTmpDir.FullName, sandboxPath);
            // add metadatum for sandboxPath
            taskItem.SetMetadata("WasmPath", sandboxPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
            if (Directory.Exists(sourcePath))
            {
                DirectoryCopy(sourcePath, destinationPath);
            }
            else if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destinationPath);
            }
            else
            {
                _log.LogMessage(MessageImportance.High, $"Task item {sourcePath} not found.");
            }
            _log.LogMessage(MessageImportance.Low, $"Copied {sourcePath} to {destinationPath}");
        }
    }
}
