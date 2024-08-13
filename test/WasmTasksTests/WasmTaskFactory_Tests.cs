using System.Reflection;
using Microsoft.Build.Framework;
using MSBuildWasm;
using Shouldly;
using static MSBuildWasm.WasmTaskFactory;

namespace WasmTasksTests
{
    // TODO add cases for invalid scenarious to demo how you handle the cases when customer does something wrong
    public class WasmTaskFactory_Tests
    {
        [Fact]
        public void BuildTaskType_CreatesTypeWithCorrectName()
        {
            const string taskName = "TestTask";
            TaskPropertyInfo[] properties = Array.Empty<TaskPropertyInfo>();

            Type resultType = WasmTaskReflectionBuilder.BuildTaskType(taskName, properties);
            ITask? task = Activator.CreateInstance(resultType) as ITask;

            Assert.Equal(taskName, resultType.Name);
            Assert.Equal(typeof(WasmTask), resultType.BaseType);
        }

        [Theory]
        [InlineData(typeof(string))]
        [InlineData(typeof(bool))]
        [InlineData(typeof(ITaskItem))]
        [InlineData(typeof(ITaskItem[]))]
        [InlineData(typeof(bool[]))]
        [InlineData(typeof(string[]))]
        public void BuildTaskType_CreatesTypeWithCorrectProperties(Type propType)
        {
            const string taskName = "TestTask";
            const string prop1name = "p1";
            const string prop2name = "p2";
            TaskPropertyInfo[] properties = new[]
            {
            new TaskPropertyInfo(prop1name, propType, false, false),
        };

            // Act
            Type resultType = WasmTaskReflectionBuilder.BuildTaskType(taskName, properties);

            // Assert
            PropertyInfo[] resultProperties = resultType.GetProperties();
            Assert.NotNull(resultType.GetProperty(prop1name));
            Assert.Null(resultType.GetProperty(prop2name));
        }
        // split/theory
        [Fact]
        public void BuildTaskType_CreatesTypeWithCorrectAttributes()
        {
            // Arrange
            const string taskName = "TestTask";
            const string prop1name = "p1";
            const string prop2name = "p2";
            TaskPropertyInfo[] properties = new[]
            {
                new TaskPropertyInfo(prop1name, typeof(string), output: true, required: false),
                new TaskPropertyInfo(prop2name, typeof(bool), false, true)
            };

            // Act
            Type resultType = WasmTaskReflectionBuilder.BuildTaskType(taskName, properties);

            // Assert
            Assert.NotNull(resultType.GetProperty(prop1name)!.GetCustomAttribute<OutputAttribute>());
            Assert.Null(resultType.GetProperty(prop1name)!.GetCustomAttribute<RequiredAttribute>());
            Assert.NotNull(resultType.GetProperty(prop2name)!.GetCustomAttribute<RequiredAttribute>());
            Assert.Null(resultType.GetProperty(prop2name)!.GetCustomAttribute<OutputAttribute>());
        }
        [Fact]
        public void ConvertJsonTaskInfoToProperties_ShouldParseProperties()
        {
            string taskInfoJson = "{ \"properties\": [  {\"name\":\"Dirs\", \"property_typexxx\": \"ITaskItemArray\", \"required\": true, \"output\": false }, {\"name\":\"MergedDir\", \"property_type\": \"ITaskItem\", \"required\": false, \"output\": true }, {\"name\":\"MergedName\", \"property_type\": \"string\", \"required\": false, \"output\": false } ] }";
            TaskPropertyInfo[] propsExpected = new TaskPropertyInfo[]
            {
                new TaskPropertyInfo("Dirs", typeof(ITaskItem[]), false, true),
                new TaskPropertyInfo("MergedDir", typeof(ITaskItem), true, false),
                new TaskPropertyInfo("MergedName", typeof(string), false, false)
            };


            TaskPropertyInfo[] propsParsed = Serializer.DeserializeTaskInfoJson(taskInfoJson);

            propsExpected.ShouldBeEquivalentTo(propsParsed);
        }

        // the task returns undeserializable json, should error
        //[Fact]
        //public void GetTaskInfo_InvalidJson_ShouldError()
        //{
        //    const string invalidJson = "{ \"Properties\": { \"Dirs\": { \"type\": \"ITaskItem[]\", \"required\": true, \"output\": false }, \"MergedDir\": { \"type\": \"ITaskItem\", \"required\": false, \"output\": true }, \"MergedName\": { \"type\": \"string\", \"required\": false, \"output\": false } ";

        //    WasmTaskFactory factory = new WasmTaskFactory();

        //    factory.OnTaskInfoReceived(null, invalidJson);





        //}

        // it's a module without exports!

        // it's a component

        // did not provide taskInfo during initialization

        // no params (ok)




        // [Fact]
        //        public void E2E_Template()
        //        {
        //            const string pathToWasmDLL = @"";
        //            const string taskPath = @"";
        //            const string pathToMSBuild = @"";

        //            using (TestEnvironment env = TestEnvironment.Create())
        //            {
        //                TransientTestFolder folder = env.CreateFolder(createFolder: true);
        //                string location = Assembly.GetExecutingAssembly().Location;
        //                TransientTestFile inlineTask = env.CreateFile(folder, "5107.proj", @$"
        //<Project>

        //  <UsingTask TaskName=""MyNewFancyTask"" AssemblyFile=""{pathToWasmDLL}"" TaskFactory=""WasmTaskFactory"">
        //    <Task>{taskPath}</Task>
        //  </UsingTask>

        //<Target Name=""ToRun"">
        //  <MyNewFancyTask/>
        //</Target>

        //</Project>
        //");
        //                // Run MSBuild and assert it's successful
        //                // TODO
        //            }
        //        }

    }
}
