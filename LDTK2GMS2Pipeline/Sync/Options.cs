using System.Text.Json;
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
            new List<string>() { "ExampleOnlyIncludedProperty" }
        },
        {
            "objExampleA_Child",
            new List<string>() { "~ExampleExcludedProperty (Forces ExampleExcludedProperty to be included in this child)" }
        }
    };

    private class IgnoredPropertyCache
    {
        public Dictionary<string, bool> Values;
        public bool IsInverted;
    }

    private Dictionary<string, IgnoredPropertyCache>? parsedIgnoredProperties = null;


    public bool IsPropertyIgnored( GMObject _propertyRoot, GMObjectProperty _property, GMObject _currentObject )
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
                    Values = new (pair.Value.Count),
                    IsInverted = isInverted
                };

                foreach (string s in pair.Value)
                {
                    bool shouldBeIncluded = s.StartsWith('~');
                    if (shouldBeIncluded)
                        cache.Values.Add(s.Substring(1), true);
                    else
                        cache.Values.Add(s, false);
                }

                parsedIgnoredProperties.Add( key, cache );
            }
        }

        while (true)
        {
            bool isRoot = _currentObject == _propertyRoot;
            if (parsedIgnoredProperties.TryGetValue(_currentObject.name, out var values))
            {
                bool gotValue = values.Values.TryGetValue(_property.varName, out bool shouldBeIncluded);
                if (gotValue)
                    return !shouldBeIncluded ^ values.IsInverted;

                if (isRoot)
                    return values.IsInverted;
            }
            if (isRoot)
                break;
            _currentObject = _currentObject.parentObjectId;
            if (_currentObject == null)
                break;
        }

        return false;
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