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
        public const string TaskItemGuestPathPropertyName = "WasmPath";
        public const string TaskItemHostPathPropertyName = "ItemSpec";

        const string PropertiesRoot = "properties";
        private const string DirectoriesRoot = "directories";

        // task info deserialization
        const string NameProperty = "name";
        const string PropertyTypeProperty = "property_type";
        const string OutputProperty = "output";
        const string RequiredProperty = "required";


        /// <summary>
        /// Serializes an ITaskItem into a dictionary of metadata.
        /// </summary>
        /// <param name="item">The ITaskItem to serialize.</param>
        /// <returns>A dictionary containing the item's metadata and ItemSpec.</returns>
        internal static Dictionary<string, string> SerializeITaskItem(ITaskItem item)
        {
            return item.MetadataNames
                .Cast<string>()
                .ToDictionary(metadata => metadata, item.GetMetadata)
                .Append(new KeyValuePair<string, string>(TaskItemHostPathPropertyName, item.ItemSpec))
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

        private static readonly HashSet<Type> s_supportedPropetyTypes = new HashSet<Type>
            {
                typeof(string),
                typeof(bool),
                typeof(ITaskItem),
                typeof(ITaskItem[]),
                typeof(string[]),
                typeof(bool[])
            };

        /// <summary>
        /// Determines if a property type is supported.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <returns>True if the type is supported, false otherwise.</returns>
        private static bool IsSupportedPropertyType(Type type)
        {
            return s_supportedPropetyTypes.Contains(type);
        }

        /// <summary>
        /// Serializes the properties of an object to JSON, excluding specified properties and properties with unsupported types.
        /// </summary>
        /// <param name="task">The task whose properties to serialize.</param>
        /// <returns>A JSON string representing the serialized properties.</returns>
        internal static string SerializeProperties(WasmTask task)
        {
            var propertiesToSerialize = task.GetType()
                                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(prop => !task._excludedPropertyNames.Contains(prop.Name) && IsSupportedPropertyType(prop.PropertyType))
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
        /// <exception cref="PropertyDeserializationException">Thrown when there's an error deserializing the property.</exception>
        private static TaskPropertyInfo DeserializePropertyInfo(JsonElement jsonProperty)
        {

            try
            {
                return new TaskPropertyInfo(
                    GetRequiredString(jsonProperty, NameProperty),
                    ConvertStringToType(GetRequiredString(jsonProperty, PropertyTypeProperty)),
                    GetRequiredBoolean(jsonProperty, OutputProperty),
                    GetRequiredBoolean(jsonProperty, RequiredProperty)
                );
            }
            catch (KeyNotFoundException ex)
            {
                throw new TaskInfoDeserializationException($"Missing property spec: {ex.Message}", ex);
            }
            catch (InvalidOperationException ex)
            {
                throw new TaskInfoDeserializationException($"Invalid property spec value: {ex.Message}", ex);
            }
        }

        private static string GetRequiredString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property))
            {
                throw new KeyNotFoundException(propertyName);
            }
            return property.GetString() ?? throw new InvalidOperationException($"{propertyName} cannot be null");
        }

        private static bool GetRequiredBoolean(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property))
            {
                throw new KeyNotFoundException(propertyName);
            }
            return property.GetBoolean();
        }

        /// <summary>
        /// Converts a string representation of a property type to its corresponding Type.
        /// </summary>
        /// <param name="type">The string representation of the property type.</param>
        /// <returns>The corresponding Type for the given property type string.</returns>
        /// <exception cref="TaskInfoDeserializationException">Thrown when an unsupported property type is provided.</exception>
        public static Type ConvertStringToType(string type)
        {
            if (!Enum.TryParse(type, true, out PropertyType propertyType))
            {
                throw new TaskInfoDeserializationException($"Unsupported property type: {type}");
            }

            return propertyType switch
            {
                PropertyType.String => typeof(string),
                PropertyType.Bool => typeof(bool),
                PropertyType.ITaskItem => typeof(ITaskItem),
                PropertyType.StringArray => typeof(string[]),
                PropertyType.BoolArray => typeof(bool[]),
                PropertyType.ITaskItemArray => typeof(ITaskItem[]),
                _ => throw new TaskInfoDeserializationException($"Unsupported transfer type: {type}")
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

            if (root.TryGetProperty(PropertiesRoot, out JsonElement properties))
            {
                foreach (JsonElement jsonProperty in properties.EnumerateArray())
                {
                    taskPropertyInfos.Add(DeserializePropertyInfo(jsonProperty));
                }
            }
            else
            {
                throw new TaskInfoDeserializationException("No \"properties\" found in task info JSON.");
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
            return $"{{\"{PropertiesRoot}\":{SerializeProperties(task)},\"{DirectoriesRoot}\":{SerializeDirectories(task.Directories)}}}";
        }

        /// <summary>
        /// Parses the provided JSON string and retrieves an enumerator for the properties
        /// of the specified root element within the JSON structure.
        /// </summary>
        /// <param name="json">The JSON string to be parsed.</param>
        /// <returns>An enumerator for the properties of the root element, allowing iteration over the JSON properties.</returns>
        internal static JsonElement.ObjectEnumerator JsonPropertiesEnumeration(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement properties = root.GetProperty(PropertiesRoot);

            return properties.EnumerateObject();
        }

        /// <summary>
        /// Retrieves the guest path from the provided task item JSON element.
        /// </summary>
        /// <param name="taskItemJsonElement">The JSON element containing task item data.</param>
        /// <returns>A string representing the guest path extracted from the task item JSON element.</returns>
        internal static string GetGuestPath(JsonElement taskItemJsonElement)
        {
            return taskItemJsonElement.GetProperty(TaskItemGuestPathPropertyName).GetString();
        }

        /// <summary>
        /// Retrieves the host path from the provided task item JSON element.
        /// </summary>
        /// <param name="taskItemJsonElement">The JSON element containing task item data.</param>
        /// <returns>A string representing the host path extracted from the task item JSON element.</returns>
        internal static string GetHostPath(JsonElement taskItemJsonElement)
        {
            return taskItemJsonElement.GetProperty(TaskItemHostPathPropertyName).GetString();
        }

        /// <summary>
        /// Exception type for errors deserializing task info.
        /// </summary>
        internal class TaskInfoDeserializationException : Exception
        {
            public TaskInfoDeserializationException()
            {
            }

            public TaskInfoDeserializationException(string message) : base(message)
            {
            }

            public TaskInfoDeserializationException(string message, Exception innerException) : base(message, innerException)
            {
            }

        }
    }
}
