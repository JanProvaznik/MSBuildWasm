// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace MSBuildWasm
{
    // TODO documentation
    internal class Serializer
    {

        internal static Dictionary<string, string> SerializeITaskItem(ITaskItem item)
        {
            return item.MetadataNames
                .Cast<string>()
                .ToDictionary(metadata => metadata, metadata => item.GetMetadata(metadata))
                .Append(new KeyValuePair<string, string>("ItemSpec", item.ItemSpec))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        internal static Dictionary<string, string>[] SerializeITaskItems(ITaskItem[] items)
        {
            return items.Select(SerializeITaskItem).ToArray();
        }
        private static bool IsSupportedType(Type type)
        {
            return type == typeof(string) || type == typeof(bool) || type == typeof(ITaskItem) || type == typeof(ITaskItem[]) || type == typeof(string[]) || type == typeof(bool[]);
        }

        /// <summary>
        /// Use reflection to gather properties of this class, and serialize them to a json.
        /// </summary>
        /// <returns>string of a json</returns>
        internal static string SerializeProperties(object o, HashSet<string> excluded)
        {
            var propertiesToSerialize = o.GetType()
                                        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                        .Where(p => !excluded.Contains(p.Name) && IsSupportedType(p.PropertyType))
                                        .ToDictionary(p => p.Name, p =>
                                        {
                                            object value = p.GetValue(o);
                                            // TODO can other types be here?
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
        internal static string SerializeDirectories(ITaskItem[] directories)
        {
            return JsonSerializer.Serialize(directories.Select(d => d.ItemSpec).ToArray());
        }
        private static TaskPropertyInfo ExtractPropertyInfo(JsonElement jsonProperty) =>
            new TaskPropertyInfo(
                jsonProperty.GetProperty("name").GetString(),
                ConvertStringToType(jsonProperty.GetProperty("property_type").GetString()),
                jsonProperty.GetProperty("output").GetBoolean(),
                jsonProperty.GetProperty("required").GetBoolean()
            );

        public enum PropertyType
        {
            String,
            Bool,
            ITaskItem,
            ITaskItemArray,
            StringArray,
            BoolArray,
        }

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

        /// <param name="json">Task Info JSON</param>
        /// <returns>List of the property infos to create in the task type.</returns>
        public static TaskPropertyInfo[] ConvertTaskInfoJsonToProperties(string json)
        {
            List<TaskPropertyInfo> taskPropertyInfos = [];
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("properties", out JsonElement properties))
            {
                foreach (JsonElement jsonProperty in properties.EnumerateArray())
                {
                    taskPropertyInfos.Add(ExtractPropertyInfo(jsonProperty));
                }
            }

            return [.. taskPropertyInfos];
        }
    }
}
