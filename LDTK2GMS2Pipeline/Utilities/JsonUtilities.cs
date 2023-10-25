using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace LDTK2GMS2Pipeline.Utilities;
public static class JsonUtilities
{
    public static T GetValue<T>(this JsonNode? _node, string _name)
    {
        if (_node == null)
            throw new JsonException("Node is null");

        var sub = _node[_name];
        if (sub == null)
            throw new JsonException($"Node is lacking property with name{_name}");

        return sub.GetValue<T>();
    }

    public static T GetValue<T>(this JsonNode? _node, string _name, T _default)
    {
        JsonNode? sub = _node?[_name];
        return sub != null ? sub.GetValue<T>() : _default;
    }

    public enum ArrayMergeMode
    {
        /// <summary>
        /// Completely replace existing array with new one
        /// </summary>
        Replace,
        /// <summary>
        /// Adds arrays together
        /// </summary>
        Concat,
        /// <summary>
        /// Replaces values inside of array elements. Size of final array will match size of merging array
        /// </summary>
        Merge
    }

    public class Options
    {
        public ArrayMergeMode ArrayMerge = ArrayMergeMode.Merge;
    }

    public static string Merge(JsonDocument originalJson, JsonDocument newContent, Options? options = null)
    {
        var outputBuffer = new ArrayBufferWriter<byte>();

        using (var jsonWriter = new Utf8JsonWriter(outputBuffer, new JsonWriterOptions { Indented = true }))
        {
            JsonElement root1 = originalJson.RootElement;
            JsonElement root2 = newContent.RootElement;

            if (root1.ValueKind != JsonValueKind.Array && root1.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"The original JSON document to merge new content into must be a container type. Instead it is {root1.ValueKind}.");
            }

            if (root1.ValueKind != root2.ValueKind)
            {
                throw new InvalidOperationException($"Types of JsonDocuments are different: {root1.ValueKind} vs {root2.ValueKind}");
            }

            options ??= new Options();

            if (root1.ValueKind == JsonValueKind.Array)
            {
                MergeArrays(jsonWriter, root1, root2, options);
            }
            else
            {
                MergeObjects(jsonWriter, root1, root2, options);
            }
        }

        return Encoding.UTF8.GetString(outputBuffer.WrittenSpan);
    }

    private static void MergeObjects(Utf8JsonWriter jsonWriter, JsonElement root1, JsonElement root2, Options options)
    {
        Debug.Assert(root1.ValueKind == JsonValueKind.Object);
        Debug.Assert(root2.ValueKind == JsonValueKind.Object);

        jsonWriter.WriteStartObject();

        // Write all the properties of the first document.
        // If a property exists in both documents, either:
        // * Merge them, if the value kinds match (e.g. both are objects or arrays),
        // * Completely override the value of the first with the one from the second, if the value kind mismatches (e.g. one is object, while the other is an array or string),
        // * Or favor the value of the first (regardless of what it may be), if the second one is null (i.e. don't override the first).
        foreach (JsonProperty property in root1.EnumerateObject())
        {
            string propertyName = property.Name;

            JsonValueKind newValueKind;

            if (root2.TryGetProperty(propertyName, out JsonElement newValue) && (newValueKind = newValue.ValueKind) != JsonValueKind.Null)
            {
                jsonWriter.WritePropertyName(propertyName);

                JsonElement originalValue = property.Value;
                JsonValueKind originalValueKind = originalValue.ValueKind;

                if (newValueKind == JsonValueKind.Object && originalValueKind == JsonValueKind.Object)
                {
                    MergeObjects(jsonWriter, originalValue, newValue, options); // Recursive call
                }
                else if (newValueKind == JsonValueKind.Array && originalValueKind == JsonValueKind.Array)
                {
                    MergeArrays(jsonWriter, originalValue, newValue, options);
                }
                else
                {
                    newValue.WriteTo(jsonWriter);
                }
            }
            else
            {
                property.WriteTo(jsonWriter);
            }
        }

        // Write all the properties of the second document that are unique to it.
        foreach (JsonProperty property in root2.EnumerateObject())
        {
            if (!root1.TryGetProperty(property.Name, out _))
            {
                property.WriteTo(jsonWriter);
            }
        }

        jsonWriter.WriteEndObject();
    }

    private static void MergeArrays(Utf8JsonWriter jsonWriter, JsonElement root1, JsonElement root2, Options options)
    {
        void Concat()
        {
            // Write all the elements from both JSON arrays
            foreach (JsonElement element in root1.EnumerateArray())
            {
                element.WriteTo(jsonWriter);
            }
            foreach (JsonElement element in root2.EnumerateArray())
            {
                element.WriteTo(jsonWriter);
            }
        }

        void Replace()
        {
            foreach (JsonElement element in root2.EnumerateArray())
            {
                element.WriteTo(jsonWriter);
            }
        }

        void Merge()
        {
            int index = 0;
            var sourceLength = root1.GetArrayLength();
            foreach (JsonElement element in root2.EnumerateArray())
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.Object:
                        if (index < sourceLength)
                            MergeObjects(jsonWriter, root1[index], element, options);
                        else
                            element.WriteTo(jsonWriter);
                        break;
                    case JsonValueKind.Array:
                        if (index < sourceLength)
                            MergeArrays(jsonWriter, root1[index], element, options);
                        else
                            element.WriteTo(jsonWriter);
                        break;
                    default:
                        element.WriteTo(jsonWriter);
                        break;
                }

                index++;
            }
        }

        Debug.Assert(root1.ValueKind == JsonValueKind.Array);
        Debug.Assert(root2.ValueKind == JsonValueKind.Array);

        jsonWriter.WriteStartArray();

        switch (options.ArrayMerge)
        {
            case ArrayMergeMode.Concat:
                Concat();
                break;
            case ArrayMergeMode.Replace:
                Replace();
                break;
            case ArrayMergeMode.Merge:
                Merge();
                break;
        }

        jsonWriter.WriteEndArray();
    }
}
