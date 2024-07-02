// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Build.UnitTests.Shared;
using MSBuildWasm;
using Xunit;
using Shouldly;
using Microsoft.Build.UnitTests;

namespace WasmTasksTests
{
    public class WasmTask_Tests
    {
        [Fact]
        public void ExecuteTemplate_WithDefaultSettings_ShouldSucceed()
        {
            IWasmTask task = new WasmTask {
                BuildEngine = new MockEngine(),
                WasmFilePath = "../../../../../examples/rust_template/target/wasm32-wasi/release/rust_template.wasm"
            };
            
            task.Execute().ShouldBeTrue();
        }

    }
}
