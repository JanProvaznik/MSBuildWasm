// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace MSBuildWasm
{
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
        private static TaskPropertyInfo ExtractPropertyInfo(JsonProperty jsonProperty)
        {
            string name = jsonProperty.Name;
            JsonElement value = jsonProperty.Value;

            string type = value.GetProperty("type").GetString();
            bool required = value.GetProperty("required").GetBoolean();
            bool output = value.GetProperty("output").GetBoolean();

            Type propertyType = ConvertStringToType(type);
            return new TaskPropertyInfo(name, propertyType, output, required);
        }
        private static Type ConvertStringToType(string type) => type switch
        {
            "string" => typeof(string),
            "bool" => typeof(bool),
            "ITaskItem" => typeof(ITaskItem),
            "string[]" => typeof(string[]),
            "bool[]" => typeof(bool[]),
            "ITaskItem[]" => typeof(ITaskItem[]),
            _ => throw new ArgumentException($"Unsupported transfer type: {type}")
        };
        /// <param name="json">Task Info JSON</param>
        /// <returns>List of the property infos to create in the task type.</returns>
        public static TaskPropertyInfo[] ConvertJsonTaskInfoToProperties(string json)
        {
            List<TaskPropertyInfo> taskPropertyInfos = [];
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.TryGetProperty("Properties", out JsonElement properties))
            {
                foreach (JsonProperty jsonProperty in properties.EnumerateObject())
                {
                    taskPropertyInfos.Add(ExtractPropertyInfo(jsonProperty));
                }
            }

            return [.. taskPropertyInfos];
        }
    }
}
