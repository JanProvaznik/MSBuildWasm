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
using Xunit;

namespace WasmTasksTests
{
    public class WasmTaskFactory_Tests
    {

        // [Fact]
        public void E2E_Template()
        {
            const string pathToWasmDLL = @"";
            const string taskPath = @"";
            const string pathToMSBuild = @"";

            using (TestEnvironment env = TestEnvironment.Create())
            {
                TransientTestFolder folder = env.CreateFolder(createFolder: true);
                string location = Assembly.GetExecutingAssembly().Location;
                TransientTestFile inlineTask = env.CreateFile(folder, "5107.proj", @$"
<Project>

  <UsingTask TaskName=""MyNewFancyTask"" AssemblyFile=""{pathToWasmDLL}"" TaskFactory=""WasmTaskFactory"">
    <Task>{taskPath}</Task>
  </UsingTask>

<Target Name=""ToRun"">
  <MyNewFancyTask/>
</Target>

</Project>
");
                // Run MSBuild and assert it's successful
                // TODO
            }
        }

    }
}
