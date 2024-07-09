// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Build.UnitTests.Shared;
using Microsoft.Build.UnitTests;
using Microsoft.Build;
using Shouldly;
using System.Diagnostics;

namespace WasmTasksTests
{
    public class WasmTaskFactory_Tests
    {

        [Fact]
        public void E2E()
        {
            Environment.SetEnvironmentVariable("MSBuildDebugUnitTests", "1");
            using (TestEnvironment env = TestEnvironment.Create())
            {
                TransientTestFolder folder = env.CreateFolder(createFolder: true);
                string location = Assembly.GetExecutingAssembly().Location;
                TransientTestFile inlineTask = env.CreateFile(folder, "5107.proj", @$"
<Project>

  <UsingTask TaskName=""NewWasmTask"" TaskFactory=""WasmTaskFactory"" AssemblyFile=""C:\Users\t-jprovaznk\dev\MSBuildWasm\src\bin\Debug\net8.0\MSBuildWasm.dll"">
    <Task>a.wasm</Task>
  </UsingTask>

<Target Name=""ToRun"">
  <NewWasmTask/>
</Target>

</Project>
");
                //Debugger.Launch();
                string output = RunnerUtilities.ExecMSBuild(@"C:\Users\t-jprovaznk\OneDrive - Microsoft\dev\MSBuild\artifacts\bin\bootstrap\net8.0\MSBuild\Current\Bin\MSBuild.exe", inlineTask.Path, out bool success);
                success.ShouldBeTrue(output);
            }
        }

    }
}
