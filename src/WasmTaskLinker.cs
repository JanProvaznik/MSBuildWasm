// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Wasmtime;

namespace MSBuildWasm
{
    public delegate void TaskInfoEventHandler(object sender, string taskInfoJson);
    internal class WasmTaskLinker : Linker
    {
        private readonly TaskLoggingHelper _log;
        /// <summary>
        /// The task requires a function "TaskInfo" to be present in the WebAssembly module, it's used only in the factory to get the task properties.
        /// </summary>

        public WasmTaskLinker(Engine engine, TaskLoggingHelper log) : base(engine)
        {
            _log = log;
        }
        /// <summary>
        /// Links logger functions to the WebAssembly module
        /// </summary>
        /// <param name="linker"></param>
        /// <param name="store"></param>
        /// <exception cref="Exception"></exception>
        public void LinkLogFunctions(Store store)
        {
            Define("msbuild-log", "LogMessage", Function.FromCallback(store, (Caller caller, int importance, int address, int length) =>
            {
                Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
                string message = memory.ReadString(address, length);
                _log.LogMessage(ImportanceFromInt(importance), message);
            }));

            Define("msbuild-log", "LogError", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
                string message = memory.ReadString(address, length);
                _log.LogError(message);
            }));

            Define("msbuild-log", "LogWarning", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
                string message = memory.ReadString(address, length);
                _log.LogWarning(message);
            }));
            _log.LogMessage(MessageImportance.Low, "Linked logger functions to WebAssembly module.");
        }
        public void LinkTaskInfo(Store store, TaskInfoEventHandler? eventHandler)
        {
            Define("msbuild-taskinfo", "TaskInfo", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
                string propertiesString = memory.ReadString(address, length);
                eventHandler?.Invoke(this, propertiesString);
            }));
        }

        /// <summary>
        ///  returns message importance according to its int value in the enum
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static MessageImportance ImportanceFromInt(int value)
        {
            return value switch
            {
                0 => MessageImportance.High,
                1 => MessageImportance.Normal,
                2 => MessageImportance.Low,
                _ => MessageImportance.Normal,
            };
        }


    }
}
