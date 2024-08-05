// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Wasmtime;

namespace MSBuildWasm
{
    public delegate void TaskInfoEventHandler(object sender, string taskInfoJson);

    /// <summary>
    /// Wasmtime linker with functions for MSBuild logging and getting task info.
    /// </summary>
    internal class WasmTaskLinker : Linker
    {
        private readonly TaskLoggingHelper _log;
        public WasmTaskLinker(Engine engine, TaskLoggingHelper log) : base(engine)
        {
            _log = log;
        }
        /// <summary>
        /// Links logger functions to the WebAssembly store
        /// </summary>
        /// <param name="store"></param>
        public void LinkLogFunctions(Store store)
        {
            Define("msbuild-log", "LogMessage", Function.FromCallback(store, (Caller caller, int importance, int address, int length) =>
            {
                _log.LogMessage(ImportanceFromInt(importance), ExtractStringFromCallerMemory(caller, address, length));
            }));

            Define("msbuild-log", "LogError", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                _log.LogError(ExtractStringFromCallerMemory(caller, address, length));
            }));

            Define("msbuild-log", "LogWarning", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                _log.LogWarning(ExtractStringFromCallerMemory(caller, address, length));
            }));
            _log.LogMessage(MessageImportance.Low, "Linked logger functions to WebAssembly module.");
        }
        public void LinkTaskInfo(Store store, TaskInfoEventHandler eventHandler)
        {
            Define("msbuild-taskinfo", "TaskInfo", Function.FromCallback(store, (Caller caller, int address, int length) =>
            {
                eventHandler?.Invoke(this, ExtractStringFromCallerMemory(caller, address, length));
            }));
        }

        private string ExtractStringFromCallerMemory(Caller caller, int address, int length)
        {
            Memory memory = caller.GetMemory("memory") ?? throw new WasmtimeException("WebAssembly module did not export a memory.");
            // TODO any ways to provide more safety here?
            return memory.ReadString(address, length);
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
