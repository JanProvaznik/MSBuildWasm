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
    public class WasmTaskFactory : ITaskFactory2
    {
        public string FactoryName => "WasmTaskFactory";
        private TaskPropertyInfo[] _parameters;
        private TaskLoggingHelper _log;
        private string _taskName;
        private Type _taskType;

        public WasmTaskFactory()
        {
            Debugger.Launch();
        }


        public Type TaskType { get; private set; } = typeof(WasmTask);

        public void CleanupTask(ITask task)
        {
            // the task might have gotten a temp directory as execution home, delete it here
        }

        public ITask CreateTask(IBuildEngine taskFactoryLoggingHost)
        {
            return Activator.CreateInstance(_taskType) as ITask;
            //return task;
        }

        public TaskPropertyInfo[] GetTaskParameters()
        {
            return _parameters;
        }
        public bool Initialize(string taskName, IDictionary<string, string> factoryIdentityParameters, IDictionary<string, TaskPropertyInfo> parameterGroup, string taskBody, IBuildEngine taskFactoryLoggingHost)
        {
            //Debugger.Launch();

            _log = new TaskLoggingHelper(taskFactoryLoggingHost, taskName)
            {
                //TaskResources = AssemblyResources.PrimaryResources this was in msbuild ???
                HelpKeywordPrefix = $"WasmTask.{taskName}."
            };
            _taskName = taskName;
            //_parameters = parameterGroup.Values.ToArray();
            BuildTaskType(parameterGroup, taskBody);

            return true;
        }
        public bool Initialize(string taskName, IDictionary<string, TaskPropertyInfo> parameterGroup, string taskBody, IBuildEngine taskFactoryLoggingHost)
        {
            return Initialize(taskName, null, parameterGroup, taskBody, taskFactoryLoggingHost);
        }

        public ITask CreateTask(IBuildEngine taskFactoryLoggingHost, IDictionary<string, string> taskIdentityParameters) => CreateTask(taskFactoryLoggingHost);

        /// <summary>
        /// This method loads the wasm module, links  without io and executes the function GetTaskInfo which callbacks with a string of json that describes the properties of the task
        /// </summary>
        /// <param name="parameterGroup">This sets the env</param>
        /// <param name="taskBody">.wasm file path</param>
        private void BuildTaskType(IDictionary<string, TaskPropertyInfo> parameterGroup, string taskBody)
        {
            var assemblyName = new AssemblyName("DynamicWasmTasks");
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
            //var simpletypebuilder = moduleBuilder.DefineType()

            var typeBuilder = moduleBuilder.DefineType(_taskName, TypeAttributes.Public, typeof(WasmTask));

            // get from module what properties

            GetWasmTaskProperties(taskBody);

            foreach (var param in _parameters)
            {
                DefineProperty(typeBuilder, param);
            }

            try
            {
            _taskType = typeBuilder.CreateType();
                object o = Activator.CreateInstance(_taskType);
                Console.WriteLine();
            }catch (Exception e)
            {

            }
            Console.WriteLine("");
        }

        private void GetWasmTaskProperties(string wasmPath)
        {
            try
            {
                using var engine = new Engine();
                using var module = Wasmtime.Module.FromFile(engine, wasmPath);
                using var linker = new Linker(engine);
                linker.DefineWasi(); // important and not documented clearly in wasmtime-dotnet!

                var wasiConfigBuilder = new WasiConfiguration();

                using var store = new Store(engine);
                store.SetWasiConfiguration(wasiConfigBuilder);
                LinkLogFunctions(linker, store);
                LinkTaskInfo(linker, store);
                LinkOutputGathering(linker, store);
                Instance instance = linker.Instantiate(store, module);
                Action fn = instance.GetAction("GetTaskInfo");
                if (fn == null)
                {
                    _log.LogError("Function 'GetTaskInfo' not found in the WebAssembly module.");
                    return;
                }

                fn.Invoke();
            }
            catch (Exception ex)
            {
                _log.LogErrorFromException(ex, true);
            }
        }

        // todo figure out requried and output annotation
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


        private TaskPropertyInfo[] JsonToProperties(string json)
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

                    Type propertyType = GetTypeFromString(type);

                    taskPropertyInfos.Add(new TaskPropertyInfo(name, propertyType, output, required));
                }
            }

            return taskPropertyInfos.ToArray();
        }

        private Type GetTypeFromString(string type)
        {
            return type.ToLower() switch
            {
                "string" => typeof(string),
                "bool" => typeof(bool),
                // TODO: ITaskItem, lists of things
                _ => throw new ArgumentException($"Unsupported type: {type}")
            };
        }

        protected void LinkTaskInfo(Linker linker, Store store)
        {
            linker.Define("msbuild-taskinfo", "TaskInfo", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new Exception("WebAssembly module did not export a memory.");
                }

                var propertiesString = memory.ReadString(address, length);
                // sets the parameters for this class
                _parameters = JsonToProperties(propertiesString);

            }));

        }

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
                _log.LogMessage(WasmTask.ImportanceFromInt(importance), message);
            }));

            linker.Define("msbuild-log", "LogError", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new Exception("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
                _log.LogError(message);
            }));

            linker.Define("msbuild-log", "LogWarning", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                var memory = caller.GetMemory("memory");
                if (memory == null)
                {
                    throw new Exception("WebAssembly module did not export a memory.");
                }

                var message = memory.ReadString(address, length);
                _log.LogWarning(message);
            }));

        }

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

            }));
        }



    }
}
