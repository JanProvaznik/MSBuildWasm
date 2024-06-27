using System;
using System.IO;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Wasmtime;

#nullable disable

namespace MSBuildWasm
{
    /// <summary>
    /// Runs a task using wasmtime-dotnet API
    /// </summary>
    public class WasmTask : Microsoft.Build.Utilities.Task, IWasmTask
    {

        [Required]
        public string WasmFilePath { get; set; }
        // public string[] Arguments { get; set; }
        // TBD
        public bool EnableTmp { get; set; } = false;

        // TBD outputs
        public string HomeDir { get; set; } = null;

        public bool InheritEnv { get; set; } = false;

        public bool EnableIO { get; set; } = true;


        public override bool Execute()
        {
            try
            {
                using var engine = new Engine();
                using var module = Module.FromFile(engine, WasmFilePath);
                using var linker = new Linker(engine);
                linker.DefineWasi(); // important and not documented clearly in wasmtime-dotnet!

                var wasiConfigBuilder = new WasiConfiguration();

                if (InheritEnv)
                {
                    wasiConfigBuilder = wasiConfigBuilder.WithInheritedEnvironment();
                }
                string tmpPath = "tmp"; // TBD
                if (EnableTmp)
                {
                    var dir = Directory.CreateDirectory(tmpPath);
                    wasiConfigBuilder = wasiConfigBuilder.WithPreopenedDirectory(dir.FullName, ".");
                }
                if (HomeDir != null)
                {
                    var dir = Directory.CreateDirectory("wasmtaskoutput");
                    wasiConfigBuilder = wasiConfigBuilder.WithPreopenedDirectory(dir.FullName, "/out");
                }

                if (EnableIO)
                {
                    wasiConfigBuilder = wasiConfigBuilder.WithStandardOutput("output.txt")
                                                         .WithStandardError("error.txt");
                }

                using var store = new Store(engine);
                store.SetWasiConfiguration(wasiConfigBuilder);
                LinkLogFunctions(linker, store);


                Instance instance = linker.Instantiate(store, module);
                Action fn = instance.GetAction("execute"); // TBD parameters

                if (fn == null)
                {
                    Log.LogError("Function 'execute' not found in the WebAssembly module.");
                    return false;
                }

                fn.Invoke();
            }
            catch (Exception ex)
            {
                Log.LogErrorFromException(ex, true);
                return false;
            }
            finally
            {
                if (EnableTmp)
                {
                    Directory.Delete("tmp", true);
                }
                if (EnableIO)
                {
                    // TODO unique filenames
                    string output = File.ReadAllText("output.txt");
                    string error = File.ReadAllText("error.txt");

                    Log.LogMessage(MessageImportance.High, $"Output: {output}");
                    Log.LogMessage(MessageImportance.Normal, $"Error: {error}");

                    File.Delete("output.txt");
                    File.Delete("error.txt");
                }
            }


            return true; // TBD return result of the function
        }


        /// <summary>
        ///  returns message importance according to its int value in the enum
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private MessageImportance ImportanceFromInt(int value)
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
        private void LinkLogFunctions(Linker linker, Store store)
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
    }

}

