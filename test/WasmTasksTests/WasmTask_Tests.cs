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
        [Fact]
        public void ExecuteTemplate_ShouldSucceed()
        {
            var task = new TemplateWasmTask();
            
            task.Execute().ShouldBeTrue();
        }


    }
}
