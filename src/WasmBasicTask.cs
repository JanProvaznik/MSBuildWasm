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
    public class WasmBasicTask : WasmTask, IWasmTask
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


        readonly string outputPath = Path.GetFullPath("output.txt");
        readonly string errorPath = Path.GetFullPath("output.txt");
        readonly string tmpPath = Path.GetFullPath("tmp");

        const string executeFunctionName = "execute";
        const string outDirName = "wasmtaskoutput";

        public WasmBasicTask()
        {

        }

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
                    var dir = Directory.CreateDirectory(outDirName);
                    wasiConfigBuilder = wasiConfigBuilder.WithPreopenedDirectory(dir.FullName, "/out");
                }
                if (EnableIO)
                {
                    wasiConfigBuilder = wasiConfigBuilder.WithStandardOutput(outputPath)
                                                         .WithStandardError(errorPath);
                }

                using var store = new Store(engine);
                store.SetWasiConfiguration(wasiConfigBuilder);
                LinkLogFunctions(linker, store);


                Instance instance = linker.Instantiate(store, module);
                Action fn = instance.GetAction(executeFunctionName);
                //dynamic instance =

                //var instancedifferent = instance.GetFunction();


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
                    Directory.Delete(tmpPath, true);
                }
                if (EnableIO)
                {
                    // TODO unique filenames
                    if (File.Exists(outputPath))
                    {
                        string output = File.ReadAllText(outputPath);
                        Log.LogMessage(MessageImportance.High, $"Output: {output}");
                        File.Delete(outputPath);
                    }

                    if (File.Exists(errorPath))
                    {
                        string error = File.ReadAllText(errorPath);
                        Log.LogMessage(MessageImportance.Normal, $"Error: {error}");
                        File.Delete(errorPath);
                    }
                }
            }


            return true; // TBD return result of the function
        }


    }

}

