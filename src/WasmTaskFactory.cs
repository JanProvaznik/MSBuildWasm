﻿// Licensed to the .NET Foundation under one or more agreements.
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
        public string FactoryName => nameof(WasmTaskFactory);
        private TaskPropertyInfo[] _taskProperties;
        private TaskLoggingHelper _log;
        private bool _taskInfoReceived = false;
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
            return _taskProperties;
        }

        public bool Initialize(string taskName, IDictionary<string, string> factoryIdentityParameters, IDictionary<string, TaskPropertyInfo> parameterGroup, string taskBody, IBuildEngine taskFactoryLoggingHost)
        {
            _log = new TaskLoggingHelper(taskFactoryLoggingHost, taskName)
            {
                HelpKeywordPrefix = $"WasmTask.{taskName}."
            };
            _taskName = taskName;
            _taskPath = Path.GetFullPath(taskBody);

            GetCustomWasmTaskProperties(); // side effect: sets _taskProperties
            TaskType = WasmTaskReflectionBuilder.BuildTaskType(taskName, _taskProperties);

            return true;
        }
        public bool Initialize(string taskName, IDictionary<string, TaskPropertyInfo> parameterGroup, string taskBody, IBuildEngine taskFactoryLoggingHost)
        {
            return Initialize(taskName, null, parameterGroup, taskBody, taskFactoryLoggingHost);
        }

        public ITask CreateTask(IBuildEngine taskFactoryLoggingHost, IDictionary<string, string> taskIdentityParameters)
        {
            return CreateTask(taskFactoryLoggingHost);
        }

        /// <summary>
        /// Gets the properties of the Task from the WebAssembly module and sets them as a class field.
        /// </summary>
        private void GetCustomWasmTaskProperties()
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
                // WASIp2: get structured info from GetTaskInfo function return value
                Action getTaskInfo = instance.GetAction(WasmTask.GetTaskInfoFunctionName);
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
            if (!_taskInfoReceived)
            {
                _log.LogError("Task info was not received from the WebAssembly module.");
            }
        }

        /// <summary>
        /// Handling callback with Task Info JSON.
        /// </summary>
        /// <remarks>WASIp2: get structured info from GetTaskInfo function output and parse it via classes from wit-bindgen and convert them to properties, this event scheme unnecessary</remarks>
        private void OnTaskInfoReceived(object sender, string taskInfoJson)
        {
            try
            {
                _taskProperties = Serializer.DeserializeTaskInfoJson(taskInfoJson);
            }
            catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is ArgumentException)
            {
                _log.LogErrorFromException(ex);
            }
            _taskInfoReceived = true;
        }

        public void CleanupTask(ITask task) { }

        /// <summary>
        /// Functions for creating a Task type from properties it should have.
        /// </summary>
        internal static class WasmTaskReflectionBuilder
        {
            private const string TaskAssemblyName = $"WasmTaskAssembly";
            private const string TaskModuleName = $"WasmTaskModule";

            /// <summary>
            /// Creates the type for the Task using reflection from the properties gathered in the factory.
            /// </summary>
            public static Type BuildTaskType(string taskName, TaskPropertyInfo[] taskProperties)
            {
                var assemblyName = new AssemblyName(TaskAssemblyName);
                var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
                ModuleBuilder moduleBuilder = assemblyBuilder.DefineDynamicModule(TaskModuleName);

                TypeBuilder typeBuilder = moduleBuilder.DefineType(taskName, TypeAttributes.Public, typeof(WasmTask));
                foreach (TaskPropertyInfo taskPropertyInfo in taskProperties)
                {
                    DefineProperty(typeBuilder, taskPropertyInfo);
                }

                return typeBuilder.CreateType();
            }

            private static void DefineProperty(TypeBuilder typeBuilder, TaskPropertyInfo prop)
            {
                FieldBuilder fieldBuilder = typeBuilder.DefineField($"_{prop.Name}", prop.PropertyType, FieldAttributes.Private);
                PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(prop.Name, PropertyAttributes.HasDefault, prop.PropertyType, null);

                DefineGetter(typeBuilder, prop, fieldBuilder, propertyBuilder);
                DefineSetter(typeBuilder, prop, fieldBuilder, propertyBuilder);
                DefineAttributes(prop, propertyBuilder);
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
        }
    }
}
