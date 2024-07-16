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
        [Fact]
        public void ExecuteConcat()
        {
            const string i1 = "i1.txt";
            const string i2 = "i2.txt";
            const string o = "wasmconcatoutput.txt";

            const string s1 = "foo";
            const string s2 = "bar";
            const string conc = s1 + s2;

            // write foo to i1
            System.IO.File.WriteAllText(i1, s1);
            // write bar to i2
            System.IO.File.WriteAllText(i2, s2);



            var task = new ConcatWasmTask() {
                InputFile1 = new TaskItem(i1),
                InputFile2 = new TaskItem(i2),
                //OutputFile = null
            };

            task.Execute().ShouldBeTrue();
            System.IO.File.ReadAllText(o).ShouldBe(conc);

        }

    }
}
