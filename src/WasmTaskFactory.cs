// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Reflection.Emit;
using Wasmtime;
using System.Text.Json;

namespace MSBuildWasm
{
    /// <summary>
    /// Class which builds a type for a Task from a Wasm module and instantiates it.
    /// </summary>
    public class WasmTaskFactory : ITaskFactory2
    {
        public string FactoryName => "WasmTaskFactory";
        private TaskPropertyInfo[] _taskParameters;
        private TaskLoggingHelper _log;
        private string _taskName;
        private string _taskPath;
        public event TaskInfoEventHandler TaskInfoEvent;

        public WasmTaskFactory()
        {
            TaskInfoEvent += OnTaskInfoReceived;
        }


        public Type TaskType { get; private set; }

        public ITask CreateTask(IBuildEngine taskFactoryLoggingHost)
        {
            var taskInstance = Activator.CreateInstance(TaskType) as WasmTask;
            taskInstance.WasmFilePath = _taskPath;
            return taskInstance;
        }

        public TaskPropertyInfo[] GetTaskParameters()
        {
            return _taskParameters;
        }

        public bool Initialize(string taskName, IDictionary<string, string> factoryIdentityParameters, IDictionary<string, TaskPropertyInfo> parameterGroup, string taskBody, IBuildEngine taskFactoryLoggingHost)
        {
            _log = new TaskLoggingHelper(taskFactoryLoggingHost, taskName)
            {
                HelpKeywordPrefix = $"WasmTask.{taskName}."
            };
            _taskName = taskName;
            _taskPath = Path.GetFullPath(taskBody);

            GetWasmTaskProperties();

            TaskType = BuildTaskType();

            return true;
        }
        public bool Initialize(string taskName, IDictionary<string, TaskPropertyInfo> parameterGroup, string taskBody, IBuildEngine taskFactoryLoggingHost)
        {
            return Initialize(taskName, null, parameterGroup, taskBody, taskFactoryLoggingHost);
        }

        public ITask CreateTask(IBuildEngine taskFactoryLoggingHost, IDictionary<string, string> taskIdentityParameters) => CreateTask(taskFactoryLoggingHost);

        /// <summary>
        /// Creates the type for the Task using reflection.
        /// </summary>
        private Type BuildTaskType()
        {
            var assemblyName = new AssemblyName("DynamicWasmTasks");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

            TypeBuilder typeBuilder = moduleBuilder.DefineType(_taskName, TypeAttributes.Public, typeof(WasmTask));

            foreach (TaskPropertyInfo param in _taskParameters)
            {
                DefineProperty(typeBuilder, param);
            }

            return typeBuilder.CreateType();
        }

        /// <summary>
        /// Causes _properties to be updated with Task Properties from the WebAssembly module.
        /// </summary>
        /// <param name="wasmPath"></param>
        private void GetWasmTaskProperties()
        {
            try
            {
                using var engine = new Engine();
                using var module = Wasmtime.Module.FromFile(engine, _taskPath);
                using var linker = new WasmTaskLinker(engine, _log);
                using var store = new Store(engine);
                linker.DefineWasi();
                linker.LinkLogFunctions(store);
                linker.LinkTaskInfo(store, TaskInfoEvent);
                Instance instance = linker.Instantiate(store, module);
                Action getTaskInfo = instance.GetAction("GetTaskInfo");
                if (getTaskInfo == null)
                {
                    _log.LogError("Function 'GetTaskInfo' not found in the WebAssembly module.");
                    return;
                }

                getTaskInfo.Invoke();
            }
            catch (WasmtimeException ex)
            {
                _log.LogErrorFromException(ex, true);
            }
        }


        private static void DefineGetter(TypeBuilder typeBuilder, TaskPropertyInfo param, FieldBuilder fieldBuilder, PropertyBuilder propertyBuilder)
        {
            MethodBuilder getMethodBuilder = typeBuilder.DefineMethod(
                $"get_{param.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                param.PropertyType,
                Type.EmptyTypes);

            ILGenerator getIL = getMethodBuilder.GetILGenerator();
            getIL.Emit(OpCodes.Ldarg_0);
            getIL.Emit(OpCodes.Ldfld, fieldBuilder);
            getIL.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getMethodBuilder);
        }

        private static void DefineSetter(TypeBuilder typeBuilder, TaskPropertyInfo param, FieldBuilder fieldBuilder, PropertyBuilder propertyBuilder)
        {
            MethodBuilder setMethodBuilder = typeBuilder.DefineMethod(
                $"set_{param.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null,
                new[] { param.PropertyType });

            ILGenerator setIL = setMethodBuilder.GetILGenerator();
            setIL.Emit(OpCodes.Ldarg_0);
            setIL.Emit(OpCodes.Ldarg_1);
            setIL.Emit(OpCodes.Stfld, fieldBuilder);
            setIL.Emit(OpCodes.Ret);

            propertyBuilder.SetSetMethod(setMethodBuilder);
        }

        private static void DefineAttributes(TaskPropertyInfo prop, PropertyBuilder propertyBuilder)
        {
            if (prop.Output)
            {
                propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(typeof(OutputAttribute).GetConstructor(Type.EmptyTypes), []));
            }

            if (prop.Required)
            {
                propertyBuilder.SetCustomAttribute(new CustomAttributeBuilder(typeof(RequiredAttribute).GetConstructor(Type.EmptyTypes), []));
            }

        }

        private static void DefineProperty(TypeBuilder typeBuilder, TaskPropertyInfo prop)
        {
            FieldBuilder fieldBuilder = typeBuilder.DefineField($"_{prop.Name}", prop.PropertyType, FieldAttributes.Private);
            PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(prop.Name, PropertyAttributes.HasDefault, prop.PropertyType, null);

            DefineGetter(typeBuilder, prop, fieldBuilder, propertyBuilder);
            DefineSetter(typeBuilder, prop, fieldBuilder, propertyBuilder);
            DefineAttributes(prop, propertyBuilder);
        }


        private static TaskPropertyInfo ExtractProperty(JsonProperty jsonProperty)
        {
            string name = jsonProperty.Name;
            JsonElement value = jsonProperty.Value;

            string type = value.GetProperty("type").GetString();
            bool required = value.GetProperty("required").GetBoolean();
            bool output = value.GetProperty("output").GetBoolean();

            Type propertyType = ConvertStringToType(type);
            return new TaskPropertyInfo(name, propertyType, output, required);
        }

        /// <param name="json">Task Info JSON</param>
        /// <returns>List of the property infos to create in the task type.</returns>
        private TaskPropertyInfo[] ConvertJsonTaskInfoToProperties(string json)
        {
            List<TaskPropertyInfo> taskPropertyInfos = [];
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                if (root.TryGetProperty("Properties", out JsonElement properties))
                {
                    foreach (JsonProperty jsonProperty in properties.EnumerateObject())
                    {
                        taskPropertyInfos.Add(ExtractProperty(jsonProperty));
                    }
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException)
            {
                _log.LogErrorFromException(ex);
            }

            return [.. taskPropertyInfos];
        }

        private static Type ConvertStringToType(string type) => type switch
        {
            "string" => typeof(string),
            "bool" => typeof(bool),
            "ITaskItem" => typeof(ITaskItem),
            "string[]" => typeof(string[]),
            "bool[]" => typeof(bool[]),
            "ITaskItem[]" => typeof(ITaskItem[]),
            _ => throw new ArgumentException($"Unsupported transfer type: {type}")
        };

        private void OnTaskInfoReceived(object sender, string taskInfoJson)
        {
            _taskParameters = ConvertJsonTaskInfoToProperties(taskInfoJson);
        }

        public void CleanupTask(ITask task) { }
    }
}
