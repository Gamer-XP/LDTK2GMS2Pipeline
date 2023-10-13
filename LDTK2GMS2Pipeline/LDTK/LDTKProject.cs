using ProjectManager;
using Spectre.Console;
using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.LDTK;

public class LDTKProject
{
    public string jsonVersion { get; set; }
    public int nextUid { get; set; }

    public bool externalLevels { get; set; } = false;
    public string identifierStyle { get; set; } = "Free";
    public int defaultGridSize { get; set; }
    public int defaultEntityWidth { get; set; }
    public int defaultEntityHeight { get; set; }

    public Definitions defs { get; set; }
    public List<Level> levels { get; set; }

    [JsonIgnore]
    public MetaData Meta { get; private set; } = null!;

    public int GetNewUid()
    {
        return nextUid++;
    }

    [JsonIgnore]
    public FileInfo ProjectPath { get; private set; } = null!;

    [JsonIgnore]
    public FileInfo MetaPath { get; private set; } = null!;

    public string ProjectDirectory => ProjectPath.DirectoryName!;

    public static async Task<LDTKProject> Load( FileInfo _file )
    {
        await using var file = File.OpenRead( _file.FullName );

        var project = await JsonSerializer.DeserializeAsync<LDTKProject>( file );
        var data = project ?? throw new JsonException( "Failed to deserialize LDTK project" );

        data.ProjectPath = _file;
        data.MetaPath = new FileInfo( Path.ChangeExtension( _file.FullName, ".meta" ) );

        if ( data.MetaPath.Exists )
        {
            await using var metaFile = File.OpenRead( data.MetaPath.FullName );
            data.Meta = await JsonSerializer.DeserializeAsync<MetaData>( metaFile ) ?? new MetaData();
        }
        else
        {
            data.Meta = new MetaData();
        }

        data.Meta.Project = project;

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
            data.levels[i] = await JsonSerializer.DeserializeAsync<LDTKProject.Level>( levelFile ) ?? level;
            data.levels[i].externalRelPath = loadedFilePath;
        }

        return project;
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
        await JsonSerializer.SerializeAsync( metaFile, Meta, new JsonSerializerOptions() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault } );
    }

    public interface ILDTKResource
    {
        public int uid { get; set; }
    }

    public sealed class MetaData
    {
        [JsonIgnore]
        public LDTKProject Project { get; internal set; }

        public Options options { get; set; } = new Options();
        public Dictionary<string, ObjectInfo> objects { get; set; } = new();
        public Dictionary<string, TilesetInfo> tilesets { get; set; } = new();
        public Dictionary<string, LevelInfo> levels { get; set; } = new();

        public T? Get<T>( string _name )
            where T : IInfoBase
        {
            return GetDictionary<T>().GetValueOrDefault( _name.ToLower() );
        }

        public bool GetOrNew<T>( string _name, out T _value )
            where T : IInfoBase, new()
        {
            string key = _name.ToLower();
            var dict = GetDictionary<T>();
            if ( dict.TryGetValue( key, out _value ) )
                return true;

            _value = new T();
            dict.Add( key, _value );
            return false;
        }

        public void Add<T>( string _name, T _info )
            where T : IInfoBase
        {
            GetDictionary<T>().Add( _name.ToLower(), _info );
        }

        public bool Remove<T>( string _name )
            where T : IInfoBase
        {
            return GetDictionary<T>().Remove( _name.ToLower() );
        }

        public void RemoveMissing<T>( IEnumerable<string> _neededKeys, Action<string>? _onRemoved = null )
            where T : IInfoBase
        {
            var dict = GetDictionary<T>();
            var removedObjectNames = dict.Keys.Except( _neededKeys, StringComparer.InvariantCultureIgnoreCase ).ToList();
            foreach ( string key in removedObjectNames )
            {
                dict.Remove( key );
                _onRemoved?.Invoke( key );
            }
        }

        public Dictionary<TKey, TValue> CreateMapping<TKey, TMapper, TValue>( GMProject _gmProject )
            where TKey : ResourceBase
            where TMapper : InfoBase<TValue>
            where TValue : ILDTKResource
        {
            Dictionary<TKey, TValue> result = new Dictionary<TKey, TValue>();

            var dict = GetDictionary<TMapper>();
            var list = Project.defs.GetList<TValue>();

            foreach ( var obj in _gmProject.GetResourcesByType<TKey>() )
            {
                int? uid = dict.GetValueOrDefault( obj.name.ToLower() )?.uid;
                if ( uid == null )
                    continue;

                var entityType = list.Find( t => t.uid == uid );
                if ( entityType == null )
                    continue;

                result.Add( (TKey) obj, entityType );
            }

            return result;
        }

        private Dictionary<Type, IDictionary>? dictCache = null;

        private Dictionary<string, T> GetDictionary<T>()
        {
            dictCache ??= new Dictionary<Type, IDictionary>
            {
                { typeof(ObjectInfo), objects },
                { typeof(TilesetInfo), tilesets },
                { typeof(LevelInfo), levels }
            };

            return (Dictionary<string, T>) dictCache[typeof( T )];

        }

        [JsonSourceGenerationOptions( DefaultIgnoreCondition = JsonIgnoreCondition.Never )]
        public class Options
        {
            public bool ImportImageXScale { get; set; } = false;
            public bool ImportImageYScale { get; set; } = false;
            public bool ImportImageAngle { get; set; } = false;
            public bool ImportImageBlend { get; set; } = false;
        }

        public interface IInfoBase
        {
            public int uid { get; set; }
        }

        public class InfoBase<TMappedTo> : IInfoBase
        {
            public int uid { get; set; }
        }

        public class ObjectInfo : InfoBase<Entity>
        {
            public Dictionary<string, PropertyInfo> Properties { get; set; } = new();

            public class PropertyInfo
            {
                public int uid { get; set; }
                public string? type { get; set; }
                public bool gotError { get; set; }
            }
        }

        public class TilesetInfo : InfoBase<Tileset>
        {

        }

        public class LevelInfo : InfoBase<Level>
        {

        }
    }

    public class Definitions
    {
        public List<Layer> layers { get; set; }
        public List<Entity> entities { get; set; }
        public List<Tileset> tilesets { get; set; }
        public List<Enum> enums { get; set; }
        public List<object> externalEnums { get; set; }
        public List<object> levelFields { get; set; }

        public List<T> GetList<T>()
            where T: ILDTKResource
        {
            if ( layers is List<T> a )
                return a;

            if ( entities is List<T> b )
                return b;

            if ( tilesets is List<T> c )
                return c;

            if ( enums is List<T> d )
                return d;

            return null!;
        }
    }

    public class Layer : ILDTKResource
    {
        public string __type { get; set; }
        public string identifier { get; set; }
        public string type { get; set; }
        public int uid { get; set; }
        public object doc { get; set; }
        public object uiColor { get; set; }
        public int gridSize { get; set; } = 16;
        public int guideGridWid { get; set; }
        public int guideGridHei { get; set; }
        public float displayOpacity { get; set; } = 1f;
        public float inactiveOpacity { get; set; } = 0.6f;
        public bool hideInList { get; set; }
        public bool hideFieldsWhenInactive { get; set; } = true;
        public bool canSelectWhenInactive { get; set; } = true;
        public bool renderInWorldView { get; set; } = true;
        public int pxOffsetX { get; set; }
        public int pxOffsetY { get; set; }
        public int parallaxFactorX { get; set; }
        public int parallaxFactorY { get; set; }
        public bool parallaxScaling { get; set; } = true;
        public List<string> requiredTags { get; set; } = new();
        public List<string> excludedTags { get; set; } = new();
        public List<object> intGridValues { get; set; } = new();
        public List<object> intGridValuesGroups { get; set; } = new();
        public List<object> autoRuleGroups { get; set; } = new();
        public object autoSourceLayerDefUid { get; set; }
        public int? tilesetDefUid { get; set; }
        public int tilePivotX { get; set; }
        public int tilePivotY { get; set; }

        public class IntGridValue
        {
            public int value { get; set; }
            public string identifier { get; set; }
            public string color { get; set; }
            public object tile { get; set; }
            public int groupUid { get; set; }
        }

        public class AutoRuleGroup
        {
            public int uid { get; set; }
            public string name { get; set; }
            public object color { get; set; }
            public object icon { get; set; }
            public bool active { get; set; }
            public bool isOptional { get; set; }
            public List<Rule> rules { get; set; } = new();
            public bool usesWizard { get; set; }
        }

        public class Rule
        {
            public int uid { get; set; }
            public bool active { get; set; } = true;
            public int size { get; set; }
            public List<int> tileIds { get; set; }
            public int alpha { get; set; }
            public double chance { get; set; }
            public bool breakOnMatch { get; set; }
            public List<int> pattern { get; set; }
            public bool flipX { get; set; }
            public bool flipY { get; set; }
            public int xModulo { get; set; }
            public int yModulo { get; set; }
            public int xOffset { get; set; }
            public int yOffset { get; set; }
            public int tileXOffset { get; set; }
            public int tileYOffset { get; set; }
            public int tileRandomXMin { get; set; }
            public int tileRandomXMax { get; set; }
            public int tileRandomYMin { get; set; }
            public int tileRandomYMax { get; set; }
            public string checker { get; set; }
            public string tileMode { get; set; }
            public double pivotX { get; set; }
            public double pivotY { get; set; }
            public int? outOfBoundsValue { get; set; }
            public bool perlinActive { get; set; }
            public int perlinSeed { get; set; }
            public double perlinScale { get; set; }
            public int perlinOctaves { get; set; }
        }
    }

    public class Enum
    {
        public string identifier { get; set; }
        public int uid { get; set; }
        public List<Value> values { get; set; } = new();
        public object iconTilesetUid { get; set; }
        public string? externalRelPath { get; set; } = null;
        public object? externalFileChecksum { get; set; }
        public List<string> tags { get; set; } = new List<string>();

        public class Value
        {
            public string id { get; set; } = string.Empty;
            public TileRect? tileRect { get; set; }
            public int color { get; set; } = 0xFFFFFF;
        }
    }

    public class Level
    {
        public string identifier { get; set; }
        public string iid { get; set; }
        public int uid { get; set; }
        public int worldX { get; set; }
        public int worldY { get; set; }
        public int worldDepth { get; set; }
        public int pxWid { get; set; }
        public int pxHei { get; set; }
        public string __bgColor { get; set; }
        public object bgColor { get; set; }
        public bool useAutoIdentifier { get; set; }
        public object bgRelPath { get; set; }
        public object bgPos { get; set; }
        public double bgPivotX { get; set; }
        public double bgPivotY { get; set; }
        public string __smartColor { get; set; }
        public object __bgPos { get; set; }
        public string? externalRelPath { get; set; }
        public List<object> fieldInstances { get; set; } = new();
        public List<Layer> layerInstances { get; set; } = new();
        public List<Neighbour> __neighbours { get; set; } = new();

        public Level GetDatalessCopy( string _projectName )
        {
            Level result = (Level) this.MemberwiseClone();

            result.fieldInstances = new List<object>();
            result.layerInstances = new List<Layer>();
            result.externalRelPath = $"{Path.GetFileNameWithoutExtension( _projectName )}/{identifier}.ldtkl";

            return result;
        }

        public class Layer
        {
            public string __identifier { get; set; }
            public string __type { get; set; }
            public int __cWid { get; set; }
            public int __cHei { get; set; }
            public int __gridSize { get; set; }
            public float __opacity { get; set; } = 1f;
            public int __pxTotalOffsetX { get; set; }
            public int __pxTotalOffsetY { get; set; }
            public int? __tilesetDefUid { get; set; }
            public string? __tilesetRelPath { get; set; }
            public string iid { get; set; }
            public int levelId { get; set; }
            public int layerDefUid { get; set; }
            public int pxOffsetX { get; set; }
            public int pxOffsetY { get; set; }
            public bool visible { get; set; }
            public List<object> optionalRules { get; set; } = new();
            public List<int> intGridCsv { get; set; } = new();
            public List<AutoLayerTile> autoLayerTiles { get; set; } = new();
            public int seed { get; set; }
            public object overrideTilesetUid { get; set; }
            public List<TileInstance> gridTiles { get; set; } = new();
            public List<EntityInstance> entityInstances { get; set; } = new();

            public class EntityInstance
            {
                public string __identifier { get; set; }
                public List<int> __grid { get; set; }
                public List<double> __pivot { get; set; }
                public List<string> __tags { get; set; }
                public TileRect? __tile { get; set; }
                public string __smartColor { get; set; }
                public string iid { get; set; }
                public int __worldX { get; set; }
                public int __worldY { get; set; }
                public int width { get; set; }
                public int height { get; set; }
                public int defUid { get; set; }
                public List<int> px { get; set; } = new();
                public List<FieldInstance> fieldInstances { get; set; } = new();

                public class FieldInstance
                {
                    public string __identifier { get; set; }
                    public string __type { get; set; }
                    public object __value { get; set; }
                    public object? __tile { get; set; }
                    public int defUid { get; set; }
                    public List<DefaultOverride> realEditorValues { get; set; }
                }
            }

            public class AutoLayerTile
            {
                public IList<int> px { get; set; }
                public IList<int> src { get; set; }
                public int f { get; set; }
                public int t { get; set; }
                public IList<int> d { get; set; }
                public int a { get; set; }
            }

            public class TileInstance
            {
                public IList<int> px { get; set; }
                public IList<int> src { get; set; }
                public int f { get; set; }
                public int t { get; set; }
                public IList<int> d { get; set; } = new List<int>();
                public float a { get; set; } = 1f;
            }
        }

        public class Neighbour
        {
            public string levelIid { get; set; }
            public string dir { get; set; }
        }
    }

    public class Entity : ILDTKResource
    {
        public string identifier { get; set; }
        public int uid { get; set; }
        public List<string> tags { get; set; } = new List<string>();
        public bool exportToToc { get; set; }
        public object doc { get; set; }
        public int width { get; set; } = 16;
        public int height { get; set; } = 16;
        public bool resizableX { get; set; } = false;
        public bool resizableY { get; set; } = false;
        public int? minWidth { get; set; }
        public int? maxWidth { get; set; }
        public int? minHeight { get; set; }
        public int? maxHeight { get; set; }
        public bool keepAspectRatio { get; set; }
        public float tileOpacity { get; set; } = 1f;
        public float fillOpacity { get; set; } = 0.08f;
        public float lineOpacity { get; set; } = 0f;
        public bool hollow { get; set; } = false;
        public string color { get; set; } = "#94D9B3";
        public string renderMode { get; set; } = "Tile";
        public bool showName { get; set; } = true;
        public int? tilesetId { get; set; }
        public string tileRenderMode { get; set; } = "FitInside";
        public TileRect? tileRect { get; set; }
        public IList<int> nineSliceBorders { get; set; } = Array.Empty<int>();
        public int maxCount { get; set; }
        public string limitScope { get; set; } = "PerLevel";
        public string limitBehavior { get; set; } = "MoveLastOne";
        public float pivotX { get; set; } = 0.5f;
        public float pivotY { get; set; } = 0.5f;
        public List<FieldDef> fieldDefs { get; set; } = new List<FieldDef>();

        public class FieldDef : ILDTKResource
        {
            public string identifier { get; set; }
            public object doc { get; set; }
            public string __type { get; set; }
            public int uid { get; set; }
            public string type { get; set; }
            public bool isArray { get; set; } = false;
            public bool canBeNull { get; set; } = true;
            public object arrayMinLength { get; set; }
            public object arrayMaxLength { get; set; }
            public string editorDisplayMode { get; set; } = "RefLinkBetweenCenters";
            public int editorDisplayScale { get; set; } = 1;
            public string editorDisplayPos { get; set; } = "Above";
            public string editorLinkStyle { get; set; } = "CurvedArrow";
            public bool editorAlwaysShow { get; set; }
            public bool editorShowInWorld { get; set; } = false;
            public bool editorCutLongValues { get; set; } = true;
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
            public bool autoChainRef { get; set; } = true;
            public bool allowOutOfLevelRef { get; set; }
            public string allowedRefs { get; set; } = "OnlySame";
            public object allowedRefsEntityUid { get; set; }
            public List<string> allowedRefTags { get; set; } = new List<string>();
            public object tilesetUid { get; set; }
        }
    }

    public class DefaultOverride : IEquatable<DefaultOverride>
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

    public class TileRect
    {
        public int tilesetUid { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
    }

    public class Tileset : ILDTKResource
    {
        public int __cWid { get; set; }
        public int __cHei { get; set; }
        public string identifier { get; set; }
        public int uid { get; set; }
        public string relPath { get; set; }
        public object embedAtlas { get; set; }
        public int pxWid { get; set; }
        public int pxHei { get; set; }
        public int tileGridSize { get; set; }
        public int spacing { get; set; }
        public int padding { get; set; }
        public List<object> tags { get; set; } = new List<object>();
        public object tagsSourceEnumUid { get; set; }
        public List<object> enumTags { get; set; } = new List<object>();
        public List<object> customData { get; set; } = new List<object>();
        public List<object> savedSelections { get; set; } = new List<object>();
        public CachedPixelData cachedPixelData { get; set; } = new CachedPixelData();

        public class CachedPixelData
        {
            public string opaqueTiles { get; set; } = string.Empty;
            public string averageColors { get; set; } = string.Empty;
        }
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