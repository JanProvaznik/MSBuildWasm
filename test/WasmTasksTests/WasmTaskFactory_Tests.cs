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
        // it's a module without exports!

        // it's a component

        // did not provide taskInfo during initialization

        // no params (ok)

    }
}
