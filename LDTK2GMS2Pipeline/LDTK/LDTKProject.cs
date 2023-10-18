using ProjectManager;
using Spectre.Console;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject : LDTKProject.IResourceContainer
{
    [JsonInclude]
    public string jsonVersion { get; private set; }

    [JsonInclude]
    public int nextUid { get; private set; }

    public bool externalLevels { get; set; } = false;
    public string identifierStyle { get; set; } = "Free";
    public int defaultGridSize { get; set; }
    public int defaultEntityWidth { get; set; }
    public int defaultEntityHeight { get; set; }

    [JsonInclude]
    public Definitions defs { get; private set; } = new();

    [JsonInclude]
    public List<Level> levels { get; private set; } = new();

    [JsonIgnore]
    private LDTKMetaData MetaData { get; set; } = null!;

    [JsonIgnore]
    public FileInfo ProjectPath { get; private set; } = null!;

    [JsonIgnore]
    public FileInfo MetaPath { get; private set; } = null!;

    public string ProjectDirectory => ProjectPath.DirectoryName!;

    ResourceCache IResourceContainer.Cache { get; } = new();

    public object GetNewUid()
    {
        return nextUid++;
    }

    public static async Task<LDTKProject> Load( FileInfo _file )
    {
        await using var file = File.OpenRead( _file.FullName );

        var project = await JsonSerializer.DeserializeAsync<LDTKProject>( file );
        var data = project ?? throw new JsonException( "Failed to deserialize LDTK project" );

        data.ProjectPath = _file;
        data.MetaPath = new FileInfo( Path.ChangeExtension( _file.FullName, ".meta" ) );

        for ( int i = 0; i < data.levels.Count; i++ )
        {
            var level = data.levels[i];
            if ( string.IsNullOrEmpty( level.externalRelPath ) )
                continue;

            string loadedFilePath = level.externalRelPath;
            var levelPath = Path.Combine( _file.DirectoryName, level.externalRelPath );
            if ( !File.Exists( levelPath ) )
                continue;

            await using var levelFile = File.OpenRead( levelPath );
            data.levels[i] = await JsonSerializer.DeserializeAsync<Level>( levelFile ) ?? level;
            data.levels[i].externalRelPath = loadedFilePath;
        }

        data.UpdateResourceCache();

        await data.LoadMetaData();

        return project;
    }

    private async Task LoadMetaData()
    {
        if ( MetaPath.Exists )
        {
            await using var metaFile = File.OpenRead( MetaPath.FullName );
            MetaData = await JsonSerializer.DeserializeAsync<LDTKMetaData>( metaFile ) ?? new LDTKMetaData();
        }
        else
        {
            MetaData = new LDTKMetaData();
        }

        MetaData.Project = this;
        this.UpdateMetaCache();
    }

    public async Task Save( FileInfo? _savePath = null )
    {
        _savePath ??= ProjectPath;

        string projectFileName = Path.GetFileNameWithoutExtension( _savePath.FullName ) + "_debug";

        identifierStyle = "Free";

        if ( externalLevels )
        {
            string levelDirectory = $"{ProjectPath.DirectoryName}\\{projectFileName}";
            Directory.CreateDirectory( levelDirectory );
            for ( int i = levels.Count - 1; i >= 0; i-- )
            {
                var projectLevel = levels[i].GetDatalessCopy( projectFileName );
                var originalPath = levels[i].externalRelPath;
                if ( originalPath != null && projectLevel.externalRelPath != originalPath )
                    File.Delete( $"{levelDirectory}\\{Path.GetFileName( originalPath )}" );

                levels[i].externalRelPath = null;
                await using var file = File.Open( $"{ProjectPath.DirectoryName}\\{projectLevel.externalRelPath}", FileMode.Create );
                await JsonSerializer.SerializeAsync( file, levels[i], new JsonSerializerOptions() { WriteIndented = true } );
                levels[i] = projectLevel;
            }
        }

        var originalContent = JsonDocument.Parse( await File.ReadAllTextAsync( ProjectPath.FullName ) );
        var mergedJson = JsonUtilities.Merge( originalContent, JsonSerializer.SerializeToDocument( this ) );

        var savePath = $"{ProjectPath.DirectoryName}\\{projectFileName}.ldtk";
        await File.WriteAllTextAsync( savePath, mergedJson );

        await using var metaFile = File.Open( Path.ChangeExtension( savePath, ".meta" ), FileMode.Create );
        await JsonSerializer.SerializeAsync( metaFile, MetaData, new JsonSerializerOptions() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault } );
    }

    public IList GetResourceList( Type _type )
    {
        if ( _type == typeof( Enum ) )
            return defs.enums;
        if ( _type == typeof( Tileset ) )
            return defs.tilesets;
        if ( _type == typeof( Entity ) )
            return defs.entities;
        if ( _type == typeof( Layer ) )
            return defs.layers;
        if ( _type == typeof( Level ) )
            return levels;
        throw new Exception( $"Unknown type: {_type}" );
    }

    public IList GetMetaList( Type _metaType )
    {
        return MetaData.GetList( _metaType );
    }

    public List<T> GetResourceList<T>()
        where T : IResource
    {
        return (List<T>) GetResourceList( typeof( T ) );
    }

    public List<T> GetMetaList<T>()
        where T : IMeta
    {
        return (List<T>) GetMetaList( typeof( T ) );
    }

    public IEnumerable<Type> GetSupportedResources()
    {
        yield return typeof( Enum );
        yield return typeof( Tileset );
        yield return typeof( Entity );
        yield return typeof( Level );
        yield return typeof( Layer );
    }

    public class Definitions
    {
        public List<Layer> layers { get; set; }
        public List<Entity> entities { get; set; }
        public List<Tileset> tilesets { get; set; }
        public List<Enum> enums { get; set; }
        public List<object> externalEnums { get; set; }
        public List<object> levelFields { get; set; }
    }

    public sealed class DefaultOverride : IEquatable<DefaultOverride>
    {
        public enum IdTypes
        {
            V_String,
            V_Int,
            V_Float,
            V_Bool
        }

        public string id { get; set; }

        [JsonPropertyName( "params" )]
        public List<object?> values { get; set; } = new List<object?>();

        [JsonIgnore]
        public IdTypes Type
        {
            get => System.Enum.Parse<IdTypes>( id );
            set => id = value.ToString();
        }

        public DefaultOverride() { }

        public DefaultOverride( IdTypes _type, params object[] _values )
        {
            Type = _type;
            values = new List<object>( _values );
        }

        public bool Equals( DefaultOverride? other )
        {
            if ( ReferenceEquals( null, other ) ) return false;
            if ( ReferenceEquals( this, other ) ) return true;
            if ( id != other.id )
                return false;

            if ( values.Count != other.values.Count )
                return false;

            for ( int i = values.Count - 1; i >= 0; i-- )
            {
                if ( !ValueEquals( values[i], other.values[i] ) )
                    return false;
            }

            return true;
        }

        public override bool Equals( object? obj )
        {
            if ( ReferenceEquals( null, obj ) ) return false;
            if ( ReferenceEquals( this, obj ) ) return true;
            if ( obj.GetType() != this.GetType() ) return false;
            return Equals( (DefaultOverride) obj );
        }

        public override int GetHashCode()
        {
            return HashCode.Combine( id, values );
        }

        public override string ToString()
        {
            return $"{string.Join( ',', values )} [{id}]";
        }
    }

    public sealed class TileRect
    {
        public int tilesetUid { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
    }

    public static bool ValueEquals( object? _left, object? _right )
    {
        if ( _left is null && _right is null )
            return true;
        if ( _left is null || _right is null )
            return false;

        string leftString = GetComparisonValue( _left );
        string rightString = GetComparisonValue( _right );

        return leftString == rightString;
    }

    private static string GetComparisonValue( object _value )
    {
        switch ( _value )
        {
            case JsonElement json:
                return json.GetRawText();
            default:
                return JsonSerializer.Serialize( _value );
        }
    }

    public static object Json2Object( JsonElement _element )
    {
        switch ( _element.ValueKind )
        {
            case JsonValueKind.Null:
                return null;
            case JsonValueKind.Undefined:
                return "undefined";
            case JsonValueKind.Number:
                return _element.GetDouble();
            case JsonValueKind.False:
                return false;
            case JsonValueKind.True:
                return true;
            case JsonValueKind.String:
                return _element.GetString();
            case JsonValueKind.Array:
                return _element.EnumerateArray()
                    .Select( o => Json2Object( o ) )
                    .ToArray();
            default:
                return _element;
        }
    }
}