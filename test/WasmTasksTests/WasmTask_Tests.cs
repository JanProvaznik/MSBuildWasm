using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.UnitTests.Shared;
using MSBuildWasm;
using Xunit;
using Shouldly;
using System.Runtime.InteropServices;
using Microsoft.Build.UnitTests;
using System.Diagnostics;

namespace WasmTasksTests
{
    public class WasmTask_Tests : IDisposable
    {
        private const string SOLUTION_ROOT_PATH = "../../../../../";
        private const string TEST_PROJECT_PATH = "../../../";
        private const string WASM_RUST_TARGET_PATH = "target/wasm32-wasi/release/";
        private static readonly string[] s_names = ["rust_template", "rust_concat2files", "rust_mergedirectories"];
        private static readonly string[] s_paths = [$"templates/content/RustWasmTaskTemplate/{s_names[0]}/", $"examples/{s_names[1]}/", $"examples/{s_names[2]}/"];

        private static readonly string s_templateFilePath;
        private static readonly string s_concatFilePath;
        private static readonly string s_mergeFilePath;

        static WasmTask_Tests()
        {
            s_templateFilePath = Path.Combine(SOLUTION_ROOT_PATH, s_paths[0], WASM_RUST_TARGET_PATH, $"{s_names[0]}.wasm");
            s_concatFilePath = Path.Combine(SOLUTION_ROOT_PATH, s_paths[1], WASM_RUST_TARGET_PATH, $"{s_names[1]}.wasm");
            s_mergeFilePath = Path.Combine(SOLUTION_ROOT_PATH, s_paths[2], WASM_RUST_TARGET_PATH, $"{s_names[2]}.wasm");
        }

        public WasmTask_Tests()
        {
            CompileRustWasm();
        }

        public void Dispose()
        {
        }

        private void CompileRustWasm()
        {
            string[] rust_test_names = [s_templateFilePath, s_concatFilePath, s_mergeFilePath];
            string manifest_suffix = "../../../";
            foreach (string name in rust_test_names)
            {
                string path = Path.GetDirectoryName(name)!;
                string cargo_toml = Path.Combine(path, manifest_suffix, "Cargo.toml");
                ExecuteCommand($"cargo build --release --target wasm32-wasi --manifest-path {cargo_toml}");
            }
        }

        [Fact]
        public void ExecuteTemplate_ShouldSucceed()
        {
            var task = new TemplateWasmTask();
            task.Execute().ShouldBeTrue();
        }

        [Theory]
        [InlineData("i1.txt", "i2.txt")]
        [InlineData(@"folder/i1.txt", @"folder/i2.txt")]
        [InlineData(@"../i1.txt", @"../i2.txt")]
        [InlineData(@"deep/folder/structure/i1.txt", @"deep/folder/structure/i2.txt")]
        public void ExecuteConcatDifferentPaths(string inputPath1, string inputPath2)
        {
            const string s1 = "foo";
            const string s2 = "bar";
            const string conc = s1 + s2;
            const string outputPath = "wasmconcatoutput.txt";

            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Environment.CurrentDirectory, inputPath1)) ?? ".");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Environment.CurrentDirectory, inputPath2)) ?? ".");

            File.WriteAllText(inputPath1, s1);
            File.WriteAllText(inputPath2, s2);

            var task = new ConcatWasmTask
            {
                InputFile1 = new TaskItem(inputPath1),
                InputFile2 = new TaskItem(inputPath2),
            };

            task.Execute().ShouldBeTrue();
            File.ReadAllText(outputPath).ShouldBe(conc);

            File.Delete(inputPath1);
            File.Delete(inputPath2);
            File.Delete(outputPath);
        }

        [Theory]
        [InlineData("dir1", "dir2")]
        [InlineData(@"folder/dir1", @"folder/dir2")]
        [InlineData(@"../dir1", @"../dir2")]
        [InlineData(@"deep/folder/structure/dir1", @"deep/folder/structure/dir2")]
        public void ExecuteDirectoryMergeDifferentPaths(string inputPath1, string inputPath2)
        {
            CreateTestDirectories(inputPath1, inputPath2);

            var task = new DirectoryMergeWasmTask
            {
                Dirs = new ITaskItem[] { new TaskItem(inputPath1), new TaskItem(inputPath2) },
                MergedName = "output_dir"
            };

            task.Execute().ShouldBeTrue();

            string outputDir = task.MergedDir!.ItemSpec;
            Directory.Exists(outputDir).ShouldBeTrue();
            Directory.GetFiles(outputDir).Length.ShouldBe(5);

            CleanupTestDirectories(inputPath1, inputPath2, outputDir);
        }

        private void CreateTestDirectories(string inputPath1, string inputPath2)
        {
            Directory.CreateDirectory(inputPath1);
            File.WriteAllText(Path.Combine(inputPath1, "file1.txt"), "file1");
            File.WriteAllText(Path.Combine(inputPath1, "file2.txt"), "file2");

            Directory.CreateDirectory(inputPath2);
            File.WriteAllText(Path.Combine(inputPath2, "file3.txt"), "file3");
            File.WriteAllText(Path.Combine(inputPath2, "file4.txt"), "file4");
            File.WriteAllText(Path.Combine(inputPath2, "file5.txt"), "file5");
        }

        private void CleanupTestDirectories(string inputPath1, string inputPath2, string outputDir)
        {
            Directory.Delete(inputPath1, true);
            Directory.Delete(inputPath2, true);
            Directory.Delete(outputDir, true);
        }

        private static void ExecuteCommand(string command)
        {
            bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                Arguments = isWindows ? $"/C {command}" : $"-c \"{command}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            try
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output)) Console.WriteLine($"Output: {output}");
                if (!string.IsNullOrEmpty(error)) Console.WriteLine($"Error: {error}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception: {ex.Message}");
            }
        }
        public class TemplateWasmTask : WasmTask
        {
            public TemplateWasmTask() : base()
            {
                WasmFilePath = WasmTask_Tests.s_templateFilePath;
                BuildEngine = new MockEngine();
            }
        }

        public class ConcatWasmTask : WasmTask
        {
            public ITaskItem? InputFile1 { get; set; }
            public ITaskItem? InputFile2 { get; set; }
            [Output]
            public ITaskItem? OutputFile { get; set; }

            public ConcatWasmTask() : base()
            {
                WasmFilePath = WasmTask_Tests.s_concatFilePath;
                BuildEngine = new MockEngine();
            }
        }

        public class DirectoryMergeWasmTask : WasmTask
        {
            public ITaskItem[]? Dirs { get; set; }
            public string? MergedName { get; set; }
            [Output]
            public ITaskItem? MergedDir { get; set; }

            public DirectoryMergeWasmTask() : base()
            {
                WasmFilePath = WasmTask_Tests.s_mergeFilePath;
                BuildEngine = new MockEngine();
            }
        }
        private class CustomRustE2ETest
        {
            private readonly string _name;
            private readonly string _templatePath;
            private readonly string _testProjectPath;

            public CustomRustE2ETest(string name, string toplevel, string executeRustCode, string getTaskInfoRustCode)
            {
                _name = name;
                _templatePath = Path.Combine(SOLUTION_ROOT_PATH, s_paths[0]);
                _testProjectPath = Path.Combine(TEST_PROJECT_PATH, "GeneratedRustTests", _name);

                CopyTemplate();
                CreateLibRs(toplevel, executeRustCode, getTaskInfoRustCode);
                Compile();
            }

            private void CopyTemplate()
            {
                if (Directory.Exists(_testProjectPath))
                {
                    Directory.Delete(_testProjectPath, true);
                }
                DirectoryCopy(_templatePath, _testProjectPath, true);
            }

            private void CreateLibRs(string topLevel, string execute, string getTaskInfo)
            {
                string contents = $@"
mod msbuild;
use msbuild::logging::{{log_warning, log_error, log_message}};
use msbuild::task_info::{{task_info, Property, PropertyType, TaskInfoStruct, TaskResult}};
use serde::{{Serialize, Deserialize}};
{topLevel}

#[no_mangle]
#[allow(non_snake_case)]
pub fn Execute() -> TaskResult
{{
{execute}
}}

#[no_mangle]
#[allow(non_snake_case)]
pub fn GetTaskInfo() 
{{
{getTaskInfo}
}}
";
                File.WriteAllText(Path.Combine(_testProjectPath, "src", "lib.rs"), contents);
            }

            private void Compile()
            {
                string cargo_toml = Path.Combine(_testProjectPath, "Cargo.toml");
                ExecuteCommand($"cargo build --release --target wasm32-wasi --manifest-path {cargo_toml}");
            }

            public string GetWasmFilePath()
            {
                return Path.Combine(_testProjectPath, WASM_RUST_TARGET_PATH, $"rust_template.wasm");
            }

            private static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
            {
                DirectoryInfo dir = new DirectoryInfo(sourceDirName);
                DirectoryInfo[] dirs = dir.GetDirectories();

                if (!Directory.Exists(destDirName))
                {
                    Directory.CreateDirectory(destDirName);
                }

                FileInfo[] files = dir.GetFiles();
                foreach (FileInfo file in files)
                {
                    string tempPath = Path.Combine(destDirName, file.Name);
                    file.CopyTo(tempPath, false);
                }

                if (copySubDirs)
                {
                    foreach (DirectoryInfo subdir in dirs)
                    {
                        string tempPath = Path.Combine(destDirName, subdir.Name);
                        DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
                    }
                }
            }
        }

        private class EmptyWasmTask : WasmTask
        {
            public EmptyWasmTask(string path) : base()
            {
                WasmFilePath = path;
                BuildEngine = new MockEngine();
            }
        }

        // what happens if the rust tries to log invalid strings
        // note that when there is an error in the test, the template .wasm is used leading to unclear messages
        [Theory]
        [InlineData("invmem_nullptr", "std::ptr::null()", "0", false)]
        [InlineData("invmem_nullptr2", "std::ptr::null()", "10", false)]
        [InlineData("invmem_longer", "c_str.as_ptr()", "1000", false)]
        [InlineData("invmem_toobigptr", "usize::MAX as *const i8", "200", true)]
        [InlineData("invmem_maxlen", "c_str.as_ptr()", "usize::MAX", true)]
        public void LogInvalidMemory(string name, string memAddress, string len, bool shouldError)
        {
            var e2e = new CustomRustE2ETest(name, @"use msbuild::logging::{{LogWarning}};"
                , $@"
let invalid_json = ""{{\""key\"": \""value\""""; 
let c_str = std::ffi::CString::new(invalid_json).unwrap();
unsafe{{
LogWarning({memAddress},{len})
}}
println!(""{{}}"", ""{{\""properties\"":{{}}}}"");
TaskResult::Success 
", "");

            var task = new EmptyWasmTask(e2e.GetWasmFilePath());
            task.Execute();


            if (shouldError)
            {
                task.Log.HasLoggedErrors.ShouldBeTrue();
            }
            else
            {
                task.Log.HasLoggedErrors.ShouldBeFalse();
            }
        }

        private class OPropTask : WasmTask
        {
            [Output]
            public string? OProp { get; set; }

            public OPropTask(string wasmPath) : base()
            {
                WasmFilePath = wasmPath;
                BuildEngine = new MockEngine();
            }
        }

        [Theory]
        [InlineData("invout_nothing", "")]
        [InlineData("invout_empty", "{}")]
        [InlineData("invout_invalidjson", "{{}")]
        [InlineData("invout_integerprop", @"{\""properties\"":{\""OProp\"":11}}")]
        [InlineData("invout_dictprop", @"{\""properties\"":{\""OProp\"":{\""a\"":\""b\""}}}")]
        [InlineData("invout_arrayprop", @"{\""properties\"":{\""OProp\"":[{\""a\"":\""b\""}]}}")] //sus
        [InlineData("invout_unspecifiedprop", @"{\""properties\"":{\""OProp\"":{}}}")]
        public void InvalidOutputShouldError(string name, string output)
        {
            var e2e = new CustomRustE2ETest(name, "", @$"println!(""{{}}"", ""{output}"");TaskResult::Success", @"
    let task_info_struct = TaskInfoStruct {
        name: String::from(""testtest""),
        properties: vec![
            Property {
                name: String::from(""OProp""),
                output: true,
                required: false,
                property_type: PropertyType::String,
            },
        ],
    };
    task_info(task_info_struct);
");

            var task = new OPropTask(e2e.GetWasmFilePath());
            task.Execute();
            task.Log.HasLoggedErrors.ShouldBeTrue();

        }

    }

}
