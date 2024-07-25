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

namespace MSBuildWasm
{
    internal class Serializer
    {

        internal static Dictionary<string, string> SerializeITaskItem(ITaskItem item)
        {
            var taskItemDict = new Dictionary<string, string>
            {
                ["ItemSpec"] = item.ItemSpec
            };
            foreach (object metadata in item.MetadataNames)
            {
                taskItemDict[metadata.ToString()] = item.GetMetadata(metadata.ToString());
            }
            return taskItemDict;
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
                                                return Serializer.SerializeITaskItem(taskItem);
                                            }
                                            else if (value is ITaskItem[] taskItemList)
                                            {
                                                return Serializer.SerializeITaskItems(taskItemList);
                                            }
                                            return value;
                                        });

            return JsonSerializer.Serialize(propertiesToSerialize);
        }
        internal static string SerializeDirectories(ITaskItem[] directories)
        {
            return JsonSerializer.Serialize(directories.Select(d => d.ItemSpec).ToArray());
        }
    }
}
