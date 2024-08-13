// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace MSBuildWasm
{
    /// <summary>
    /// Provides serialization functionality for MSBuild task properties for transferring info between .NET task and Wasm module.
    /// </summary>
    internal class Serializer
    {
        /// <summary>
        /// Serializes an ITaskItem into a dictionary of metadata.
        /// </summary>
        /// <param name="item">The ITaskItem to serialize.</param>
        /// <returns>A dictionary containing the item's metadata and ItemSpec.</returns>
        internal static Dictionary<string, string> SerializeITaskItem(ITaskItem item)
        {
            return item.MetadataNames
                .Cast<string>()
                .ToDictionary(metadata => metadata, metadata => item.GetMetadata(metadata))
                .Append(new KeyValuePair<string, string>("ItemSpec", item.ItemSpec))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        /// <summary>
        /// Serializes an array of ITaskItems into an array of dictionaries.
        /// </summary>
        /// <param name="items">The array of ITaskItems to serialize.</param>
        /// <returns>An array of dictionaries, each containing an item's metadata and ItemSpec.</returns>
        internal static Dictionary<string, string>[] SerializeITaskItems(ITaskItem[] items)
        {
            return items.Select(SerializeITaskItem).ToArray();
        }

        /// <summary>
        /// Determines if a type is supported for serialization.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>True if the type is supported, false otherwise.</returns>
        private static bool IsSupportedType(Type type)
        {
            // TODO it's easy to miss handling new added types here, in ConvertStringToType, ReflectJsonPropertyToClassProperty and in PropertyType :D
            // please think on how to make it more robust/universal
            return type == typeof(string) || type == typeof(bool) || type == typeof(ITaskItem) || type == typeof(ITaskItem[]) || type == typeof(string[]) || type == typeof(bool[]);
        }

        /// <summary>
        /// Serializes the properties of an object to JSON, excluding specified properties and properties with unsupported types.
        /// </summary>
        /// <param name="task">The task whose properties to serialize.</param>
        /// <returns>A JSON string representing the serialized properties.</returns>
        internal static string SerializeProperties(WasmTask task)
        {
            // TODO any null checks or try/catch?
            var propertiesToSerialize = task.GetType()
                                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(prop => !task._excludedPropertyNames.Contains(prop.Name) && IsSupportedType(prop.PropertyType))
                                        .ToDictionary(prop => prop.Name, prop =>
                                        {
                                            // unsupported types are already filtered out
                                            object value = prop.GetValue(task);
                                            if (value is ITaskItem taskItem)
                                            {
                                                return SerializeITaskItem(taskItem);
                                            }
                                            else if (value is ITaskItem[] taskItemList)
                                            {
                                                return SerializeITaskItems(taskItemList);
                                            }
                                            return value;
                                        });

            return JsonSerializer.Serialize(propertiesToSerialize);
        }

        /// <summary>
        /// Serializes an array of ITaskItems representing directories to a JSON string.
        /// </summary>
        /// <param name="directories">The array of ITaskItems representing directories.</param>
        /// <returns>A JSON string containing the ItemSpecs of the directories.</returns>
        internal static string SerializeDirectories(ITaskItem[] directories) => JsonSerializer.Serialize(directories.Select(d => d.ItemSpec).ToArray());

        /// <summary>
        /// Deserializes property information from a JSON element.
        /// </summary>
        /// <param name="jsonProperty">The JSON element containing property information.</param>
        /// <returns>A TaskPropertyInfo object representing the extracted information.</returns>
        private static TaskPropertyInfo DeserializePropertyInfo(JsonElement jsonProperty) =>

            // TODO what if customer makes a typo in property name?
            // According to the tests, it fails with System.Collections.Generic.KeyNotFoundException : The given key was not present in the dictionary.
            // Please provide a better handling: maybe a custom exception with a message that the property name is not correct

            // TODO constants for property names?
            new TaskPropertyInfo(
                jsonProperty.GetProperty("name").GetString(),
                ConvertStringToType(jsonProperty.GetProperty("property_type").GetString()),
                jsonProperty.GetProperty("output").GetBoolean(),
                jsonProperty.GetProperty("required").GetBoolean()
            );

        /// <summary>
        /// Represents the supported property types for serialization.
        /// </summary>
        public enum PropertyType
        {
            String,
            Bool,
            ITaskItem,
            ITaskItemArray,
            StringArray,
            BoolArray,
        }

        /// <summary>
        /// Converts a string representation of a property type to its corresponding Type.
        /// </summary>
        /// <param name="type">The string representation of the property type.</param>
        /// <returns>The corresponding Type for the given property type string.</returns>
        /// <exception cref="ArgumentException">Thrown when an unsupported property type is provided.</exception>
        public static Type ConvertStringToType(string type)
        {
            if (!Enum.TryParse(type, true, out PropertyType propertyType))
            {
                throw new ArgumentException($"Unsupported property type: {type}");
            }

            return propertyType switch
            {
                PropertyType.String => typeof(string),
                PropertyType.Bool => typeof(bool),
                PropertyType.ITaskItem => typeof(ITaskItem),
                PropertyType.StringArray => typeof(string[]),
                PropertyType.BoolArray => typeof(bool[]),
                PropertyType.ITaskItemArray => typeof(ITaskItem[]),
                _ => throw new ArgumentException($"Unsupported transfer type: {type}")
            };
        }

        /// <summary>
        /// Converts a JSON string containing task information into an array of TaskPropertyInfo objects.
        /// </summary>
        /// <param name="json">The JSON string containing task information.</param>
        /// <returns>An array of TaskPropertyInfo objects representing the properties defined in the JSON.</returns>
        public static TaskPropertyInfo[] DeserializeTaskInfoJson(string json)
        {
            List<TaskPropertyInfo> taskPropertyInfos = [];
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            // TODO if there is no properties, should we notify the user?
            // TODO you are searching for the same key words in multiple places, maybe extract it to a file with constants?
            if (root.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonElement jsonProperty in properties.EnumerateArray())
                {
                    taskPropertyInfos.Add(DeserializePropertyInfo(jsonProperty));
                }
            }

            return [.. taskPropertyInfos];
        }
        /// <summary>
        /// Serializes the task parameters to a format recognized by the wasm module.
        /// </summary>
        /// <param name="task">Task whose parameters to serialize.</param>
        /// <returns>JSON string representing the task inputs.</returns>
        internal static string SerializeTaskInput(WasmTask task)
        {
            var sb = new StringBuilder();
            sb.Append("{\"properties\":");
            sb.Append(SerializeProperties(task));
            sb.Append(",\"directories\":");
            sb.Append(SerializeDirectories(task.Directories));
            sb.Append('}');

            return sb.ToString();

        }
    }
}
