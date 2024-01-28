using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using LDTK2GMS2Pipeline.Sync;
using LDTK2GMS2Pipeline.Utilities;

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

    [JsonIgnore]
    public Options Options { get; private set; } = new ();

    public string ProjectDirectory => ProjectPath.DirectoryName!;

    ResourceCache IResourceContainer.Cache { get; } = new();

    public object GetNewUid(IResource _resource )
    {
        return nextUid++;
    }

    public static async Task<LDTKProject> Load( FileInfo _file )
    {
        if ( _file is null )
            throw new Exception( "LDTK project not found" );

        await using var file = File.OpenRead( _file.FullName );

        var project = await JsonSerializer.DeserializeAsync<LDTKProject>( file );
        var data = project ?? throw new JsonException( "Failed to deserialize LDTK project" );

        data.ProjectPath = _file;
        data.MetaPath = new FileInfo( Path.ChangeExtension( _file.FullName, ".meta" ) );

        data.Options = await Sync.Options.Load(_file.FullName);

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

    public async Task Save( string _nameSuffix = "" )
    {
        string projectFileName = Path.GetFileNameWithoutExtension( ProjectPath.FullName ) + _nameSuffix;

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

        await SaveMeta(savePath);

        await Options.Save(savePath);
    }

    public Task SaveMeta( bool _backup = true )
    {
        return SaveMeta(MetaPath.FullName, _backup);
    }

    public async Task SaveMeta( string _savePath, bool _backup = true )
    {
        _savePath = Path.ChangeExtension(_savePath, ".meta");
        
        if (_backup && File.Exists(_savePath))
        {
            var projectName = Path.GetFileNameWithoutExtension(_savePath);
            var dirPath = Path.GetDirectoryName(_savePath);

            var path = Path.Combine(dirPath, projectName, "backups", "meta");
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            path = Path.Combine(path, $"{projectName}_{DateTime.Now:yy_MM_dd_HH_mm_ss}.meta");
            File.Move(_savePath, path);
        }

        await using var metaFile = File.Open( _savePath, FileMode.Create );
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
        public List<LevelFieldDef> levelFields { get; set; }
    }

    public sealed class LevelFieldDef
    {
        public string identifier { get; set; }
        public string? doc { get; set; }
        public string __type { get; set; }
        public int uid { get; set; }
        public string type { get; set; }
        public bool isArray { get; set; }
        public bool canBeNull { get; set; }
        public int? arrayMinLength { get; set; }
        public int? arrayMaxLength { get; set; }
        public string editorDisplayMode { get; set; }
        public int editorDisplayScale { get; set; }
        public string editorDisplayPos { get; set; }
        public string editorLinkStyle { get; set; }
        public object editorDisplayColor { get; set; }
        public bool editorAlwaysShow { get; set; }
        public bool editorShowInWorld { get; set; }
        public bool editorCutLongValues { get; set; }
        public object editorTextSuffix { get; set; }
        public object editorTextPrefix { get; set; }
        public bool useForSmartColor { get; set; }
        public object min { get; set; }
        public object max { get; set; }
        public object regex { get; set; }
        public object acceptFileTypes { get; set; }
        public DefaultOverride? defaultOverride { get; set; }
        public object textLanguageMode { get; set; }
        public bool symmetricalRef { get; set; }
        public bool autoChainRef { get; set; }
        public bool allowOutOfLevelRef { get; set; }
        public string allowedRefs { get; set; }
        public object allowedRefsEntityUid { get; set; }
        public List<object> allowedRefTags { get; set; }
        public object tilesetUid { get; set; }
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

        public bool TryGet<T>( out T? _value )
        {
            if (values.Count == 0)
            {
                _value = default;
                return false;
            }

            try
            {
                _value = (T?) Convert.ChangeType( values[0]?.ToString(), typeof( T ) );
                return true;
            }
            catch (Exception e)
            {
                _value = default;
                return false;
            }
        }

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

        if (leftString == rightString)
            return true;

        return _left.ToString() == _right.ToString();
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