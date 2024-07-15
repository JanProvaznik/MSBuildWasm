// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
#if FEATURE_APPDOMAIN
using System.Threading.Tasks;
#endif

using Microsoft.Build.BackEnd.Components.RequestBuilder;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;
using System.IO;
using ElementLocation = Microsoft.Build.Construction.ElementLocation;
using Microsoft.Build.Utilities;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
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
        private IDictionary<string, string> _executionProperties;

        public WasmTaskFactory()
        {
        }

        public Type TaskType { get; private set; }

        public void CleanupTask(ITask task)
        {
        }
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
            // TODO parameters setting up the env

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
            var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

            var typeBuilder = moduleBuilder.DefineType(_taskName, TypeAttributes.Public, typeof(WasmTask));

            foreach (var param in _taskParameters)
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
                using var linker = new Linker(engine);
                using var store = new Store(engine);
                linker.DefineWasi();
                LinkRequiredCallbacks(linker, store);
                LinkTaskInfo(linker, store);
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

        // TODO figure out Requried and Output annotation
        private void DefineProperty(TypeBuilder typeBuilder, TaskPropertyInfo param)
        {
            var fieldBuilder = typeBuilder.DefineField($"_{param.Name}", param.PropertyType, FieldAttributes.Private);

            var propertyBuilder = typeBuilder.DefineProperty(param.Name, PropertyAttributes.HasDefault, param.PropertyType, null);

            var getMethodBuilder = typeBuilder.DefineMethod(
                $"get_{param.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                param.PropertyType,
                Type.EmptyTypes);

            var getIL = getMethodBuilder.GetILGenerator();
            getIL.Emit(OpCodes.Ldarg_0);
            getIL.Emit(OpCodes.Ldfld, fieldBuilder);
            getIL.Emit(OpCodes.Ret);

            var setMethodBuilder = typeBuilder.DefineMethod(
                $"set_{param.Name}",
                MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig,
                null,
                new[] { param.PropertyType });

            var setIL = setMethodBuilder.GetILGenerator();
            setIL.Emit(OpCodes.Ldarg_0);
            setIL.Emit(OpCodes.Ldarg_1);
            setIL.Emit(OpCodes.Stfld, fieldBuilder);
            setIL.Emit(OpCodes.Ret);

            propertyBuilder.SetGetMethod(getMethodBuilder);
            propertyBuilder.SetSetMethod(setMethodBuilder);

        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="json"></param>
        /// <returns></returns>
        private TaskPropertyInfo[] ConvertJsonTaskInfoToProperties(string json)
        {
            var taskPropertyInfos = new List<TaskPropertyInfo>();

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("Properties", out JsonElement properties))
            {
                foreach (JsonProperty property in properties.EnumerateObject())
                {
                    string name = property.Name;
                    JsonElement value = property.Value;

                    string type = value.GetProperty("type").GetString();
                    bool required = value.GetProperty("required").GetBoolean();
                    bool output = value.GetProperty("output").GetBoolean();

                    Type propertyType = ConvertStringToType(type);

                    taskPropertyInfos.Add(new TaskPropertyInfo(name, propertyType, output, required));
                }
            }

            return taskPropertyInfos.ToArray();
        }

        private Type ConvertStringToType(string type)
        {
            return type.ToLower() switch
            {
                "string" => typeof(string),
                "bool" => typeof(bool),
                // TODO: ITaskItem, lists of things
                _ => throw new ArgumentException($"Unsupported type: {type}")
            };
        }

        private void LinkTaskInfo(Linker linker, Store store)
        {
            linker.Define("msbuild-taskinfo", "TaskInfo", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory") ?? throw new Exception("WebAssembly module did not export a memory.");
                var propertiesString = memory.ReadString(address, length);
                _taskParameters = ConvertJsonTaskInfoToProperties(propertiesString);
            }));

        }
        // when running a module these functions have to be exported by the host but in the factory we run it only to get TaskProperties
        private void LinkRequiredCallbacks(Linker linker, Store store)
        {
            linker.Define("msbuild-log", "LogMessage", Function.FromCallback(store, (Caller caller, int importance, int address, int length) => { }));
            linker.Define("msbuild-log", "LogError", Function.FromCallback(store, (Caller caller, int address, int length) => { }));
            linker.Define("msbuild-log", "LogWarning", Function.FromCallback(store, (Caller caller, int address, int length) => { }));
        }

    }
}
