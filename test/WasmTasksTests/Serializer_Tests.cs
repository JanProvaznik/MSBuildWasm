// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Build.Framework;
using MSBuildWasm;
using Shouldly;

namespace WasmTasksTests
{
    public class Serializer_Tests
    {


        [Fact]
        public void ConvertJsonTaskInfoToProperties_ShouldParseProperties()
        {
            string taskInfoJson = "{ \"properties\": [  {\"name\":\"Dirs\", \"property_type\": \"ITaskItemArray\", \"required\": true, \"output\": false }, {\"name\":\"MergedDir\", \"property_type\": \"ITaskItem\", \"required\": false, \"output\": true }, {\"name\":\"MergedName\", \"property_type\": \"string\", \"required\": false, \"output\": false } ] }";
            TaskPropertyInfo[] propsExpected = new TaskPropertyInfo[]
            {
                new TaskPropertyInfo("Dirs", typeof(ITaskItem[]), false, true),
                new TaskPropertyInfo("MergedDir", typeof(ITaskItem), true, false),
                new TaskPropertyInfo("MergedName", typeof(string), false, false)
            };


            TaskPropertyInfo[] propsParsed = Serializer.DeserializeTaskInfoJson(taskInfoJson);

            propsExpected.ShouldBeEquivalentTo(propsParsed);
        }

        [Fact]
        public void ConvertJsonTaskInfoToProperties_ShouldErrorOnInvalidType()
        {
            string taskInfoJson = "{ \"properties\": [  {\"_name\":\"Dirs\", \"property_typexxxx\": \"ITaskItemArray\", \"required\": true, \"output\": false }, {\"name\":\"MergedDir\", \"property_type\": \"ITaskItem\", \"required\": false, \"output\": true }, {\"name\":\"MergedName\", \"property_type\": \"string\", \"required\": false, \"output\": false } ] }";

            Should.Throw<Serializer.TaskInfoDeserializationException>(() => Serializer.DeserializeTaskInfoJson(taskInfoJson));
        }


        [Fact]
        public void DeserializeTaskInfoJson_EmptyProperties_ReturnsEmptyArray()
        {
            // Arrange
            string json = @"{""properties"": []}";

            // Act
            TaskPropertyInfo[] result = Serializer.DeserializeTaskInfoJson(json);

            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void DeserializeTaskInfoJson_MissingPropertiesRoot_ThrowsTaskInfoDeserializationException()
        {
            // Arrange
            string json = @"{""notProperties"": []}";

            // Act & Assert
            Serializer.TaskInfoDeserializationException exception = Assert.Throws<Serializer.TaskInfoDeserializationException>(() =>
                Serializer.DeserializeTaskInfoJson(json));
            Assert.Contains("No \"properties\" found in task info JSON", exception.Message);
        }

        [Fact]
        public void DeserializeTaskInfoJson_InvalidPropertyInArray_ThrowsTaskInfoDeserializationException()
        {
            // Arrange
            string json = @"{
                ""properties"": [
                    {
                        ""name"": ""ValidProperty"",
                        ""property_type"": ""String"",
                        ""output"": false,
                        ""required"": true
                    },
                    {
                        ""name"": ""InvalidProperty"",
                        ""property_type"": ""InvalidType"",
                        ""output"": false,
                        ""required"": true
                    }
                ]
            }";

            // Act & Assert
            var exception = Should.Throw<Serializer.TaskInfoDeserializationException>(() =>
                Serializer.DeserializeTaskInfoJson(json));
            Assert.Contains("Unsupported property type:", exception.Message);
        }

        [Theory]
        [InlineData("String", typeof(string))]
        [InlineData("Bool", typeof(bool))]
        [InlineData("ITaskItem", typeof(ITaskItem))]
        [InlineData("ITaskItemArray", typeof(ITaskItem[]))]
        [InlineData("StringArray", typeof(string[]))]
        [InlineData("BoolArray", typeof(bool[]))]
        public void DeserializeTaskInfoJson_AllValidPropertyTypes_ReturnsCorrectTypes(string propertyTypeString, Type expectedType)
        {
            // Arrange
            string json = $@"{{
                ""properties"": [
                    {{
                        ""name"": ""TestProperty"",
                        ""property_type"": ""{propertyTypeString}"",
                        ""output"": false,
                        ""required"": true
                    }}
                ]
            }}";

            // Act
            TaskPropertyInfo[] result = Serializer.DeserializeTaskInfoJson(json);

            // Assert
            Assert.Single(result);
            Assert.Equal(expectedType, result[0].PropertyType);
        }

        [Fact]
        public void DeserializeTaskInfoJson_MissingRequiredField_ThrowsTaskInfoDeserializationException()
        {
            // Arrange
            string json = @"{
                ""properties"": [
                    {
                        ""name"": ""TestProperty"",
                        ""property_type"": ""String"",
                        ""output"": false
                        
                    }
                ]
            }";

            // Act & Assert
            Serializer.TaskInfoDeserializationException exception = Should.Throw<Serializer.TaskInfoDeserializationException>(() =>
                Serializer.DeserializeTaskInfoJson(json));
            Assert.Contains("Missing property spec", exception.Message);
        }

        [Fact]
        public void DeserializeTaskInfoJson_InvalidJsonFormat_ThrowsJsonException()
        {
            // Arrange
            string invalidJson = "{invalid_json}";

            // Act & Assert
            Should.Throw<JsonException>(() => Serializer.DeserializeTaskInfoJson(invalidJson));
        }

        [Fact]
        public void DeserializeTaskInfoJson_NullPropertyValue_ThrowsTaskInfoDeserializationException()
        {
            // Arrange
            string json = @"{
                ""properties"": [
                    {
                        ""name"": null,
                        ""property_type"": ""String"",
                        ""output"": false,
                        ""required"": true
                    }
                ]
            }";

            // Act & Assert
            Serializer.TaskInfoDeserializationException exception = Assert.Throws<Serializer.TaskInfoDeserializationException>(() =>
                Serializer.DeserializeTaskInfoJson(json));
            Assert.Contains("Invalid property spec value", exception.Message);
        }
    }
}
