// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.UnitTests.Shared;
using MSBuildWasm;
using Xunit;
using Shouldly;
using Microsoft.Build.UnitTests;
using System.Reflection;

namespace WasmTasksTests
{
    public class WasmTask_Tests
    {
        public class TemplateWasmTask : WasmTask
        {
            public TemplateWasmTask() : base()
            {
                // set some default values
                WasmFilePath = "../../../../../templates/content/RustWasmTaskTemplate/rust_template/target/wasm32-wasi/release/rust_template.wasm";
                BuildEngine = new MockEngine();
            }
        }
        public class ConcatWasmTask : WasmTask
        {
            public ITaskItem InputFile1 { get; set; }
            public ITaskItem InputFile2 { get; set; }
            [Output]
            public ITaskItem OutputFile { get; set; }
            public ConcatWasmTask() : base()
            {
                // set some default values
                WasmFilePath = "../../../../../examples/rust_concat2files/target/wasm32-wasi/release/rust_concat2files.wasm";
                BuildEngine = new MockEngine();
            }
        }

        public class DirectoryMergeWasmTask : WasmTask
        {
            public ITaskItem[] Dirs { get; set; }
            public string MergedName { get; set; }
            [Output]
            public ITaskItem MergedDir { get; set; }
            public DirectoryMergeWasmTask() : base()
            {
                // set some default values
                WasmFilePath = "../../../../../examples/rust_mergedirectories/target/wasm32-wasi/release/rust_mergedirectories.wasm";
                BuildEngine = new MockEngine();
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

            // Ensure directory structure exists
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Environment.CurrentDirectory, inputPath1)) ?? ".");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(Environment.CurrentDirectory, inputPath2)) ?? ".");


            File.WriteAllText(inputPath1, s1);
            File.WriteAllText(inputPath2, s2);

            var task = new ConcatWasmTask()
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

        // directory merge task
        [Theory]
        [InlineData("dir1", "dir2")]
        [InlineData(@"folder/dir1", @"folder/dir2")]
        [InlineData(@"../dir1", @"../dir2")]
        [InlineData(@"deep/folder/structure/dir1", @"deep/folder/structure/dir2")]
        public void ExecuteDirectoryMergeDifferentPaths(string inputPath1, string inputPath2)
        {
            // let's have a task that takes TaskItem[] and a string inputs and outputs a one ITaskItem which is the directory

            // create the directories
            Directory.CreateDirectory(inputPath1);
            // fill the directory with 2 files
            File.WriteAllText(Path.Combine(inputPath1, "file1.txt"), "file1");
            File.WriteAllText(Path.Combine(inputPath1, "file2.txt"), "file2");
            Directory.CreateDirectory(inputPath2);
            // fill the directory with 3 files
            File.WriteAllText(Path.Combine(inputPath2, "file3.txt"), "file3");
            File.WriteAllText(Path.Combine(inputPath2, "file4.txt"), "file4");
            File.WriteAllText(Path.Combine(inputPath2, "file5.txt"), "file5");

            var task = new DirectoryMergeWasmTask()
            {
                Dirs = new ITaskItem[] { new TaskItem(inputPath1), new TaskItem(inputPath2) },
                MergedName = "output_dir"
            };

            task.Execute().ShouldBeTrue();

            string outputDir = task.MergedDir.ItemSpec;
            Directory.Exists(outputDir).ShouldBeTrue();
            Directory.GetFiles(outputDir).Length.ShouldBe(5);


            // TODO formatting

            // cleanup
            Directory.Delete(inputPath1,true);
            Directory.Delete(inputPath2, true);
            Directory.Delete(outputDir, true);


        }


    }
}
