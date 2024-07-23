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
                WasmFilePath = "../../../../../examples/rust_template/target/wasm32-wasi/release/rust_template.wasm";
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
                WasmFilePath = "../../../../../examples/rust_files/target/wasm32-wasi/release/rust_files.wasm";
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

    }
}
