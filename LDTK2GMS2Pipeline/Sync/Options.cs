using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Threading.Tasks;
using LDTK2GMS2Pipeline.LDTK;
using Spectre.Console;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

[System.Serializable]
public class Options
{
    public string LevelObjectTag { get; set; } = "Room Asset";
    
    public Dictionary<string, List<string>> IgnoredProperties { get; set; } = new()
    {
        { 
            "objExampleA", 
            new List<string>() { "ExampleExcludedProperty" }
        },
        {
            "~objExampleB",
            new List<string>() { "ExampleIncludedProperty" }
        },
    };

    private class IgnoredPropertyCache
    {
        public HashSet<string> Values;
        public bool IsInverted;
    }

    private Dictionary<string, IgnoredPropertyCache>? parsedIgnoredProperties = null;


    public bool IsPropertyIgnored( GMObject _object, GMObjectProperty _property )
    {
        if ( parsedIgnoredProperties == null )
        {
            parsedIgnoredProperties = new();
            foreach ( var pair in IgnoredProperties )
            {
                var key = pair.Key;
                bool isInverted = key.StartsWith('~');
                if (isInverted)
                    key = key.Substring(1);

                if (parsedIgnoredProperties.ContainsKey(key))
                    throw new Exception($"Key {key} contains a duplicate in options file. Resolve the conflict first.");

                IgnoredPropertyCache cache = new()
                {
                    Values = new HashSet<string>(pair.Value),
                    IsInverted = isInverted
                };

                parsedIgnoredProperties.Add( key, cache );
            }
        }

        if (!parsedIgnoredProperties.TryGetValue(_object.name, out var values))
            return false;

        return values.Values.Contains(_property.varName) ^ values.IsInverted;
    }

    private static string ConvertFilename( string _projectPath )
    {
        return Path.ChangeExtension( _projectPath, ".ini" );
    }

    public static async Task<Options> Load( string _projectPath )
    {
        _projectPath = ConvertFilename(_projectPath);
        if (!File.Exists(_projectPath))
            return new Options();

        try
        {
            await using var file = File.OpenRead(_projectPath);
            return await JsonSerializer.DeserializeAsync<Options>( file, new JsonSerializerOptions() ) ?? throw new Exception("Error parsing options");
        }
        catch
        {
            AnsiConsole.MarkupLineInterpolated( $"[red]Error reading options file. To prevent further errors, processing is stopped till errors are fixed.[/]" );
            throw;
        }
    }

    public async Task Save( string _projectPath)
    {
        _projectPath = ConvertFilename( _projectPath );
        await using var file = File.Open( _projectPath, FileMode.Create );
        await JsonSerializer.SerializeAsync(file, this, new JsonSerializerOptions() { WriteIndented = true });
    }
}