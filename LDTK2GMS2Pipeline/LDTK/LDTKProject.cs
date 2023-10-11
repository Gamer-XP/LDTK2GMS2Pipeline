using System.Buffers.Text;
using System.Collections;
using ProjectManager;
using System.Dynamic;
using System.Numerics;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Xml.Linq;

namespace LDTK2GMS2Pipeline.LDTK;

public class LDTKProject
{
    public string jsonVersion { get; set; }
    public int nextUid { get; set; }

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

    public static async Task<LDTKProject> Load( FileInfo _file )
    {
        await using var file = File.OpenRead( _file.FullName );

        var json = await JsonSerializer.DeserializeAsync<LDTKProject>( file );
        var data = json ?? throw new JsonException( "Failed to deserialize LDTK project" );

        data.ProjectPath = _file;
        data.MetaPath = new FileInfo(Path.ChangeExtension( _file.FullName, ".meta") );

        if (data.MetaPath.Exists)
        {
            await using var metaFile = File.OpenRead(data.MetaPath.FullName);
            data.Meta = await JsonSerializer.DeserializeAsync<MetaData>(metaFile) ?? new MetaData();
        }
        else
        {
            data.Meta = new MetaData();
        }

        for ( int i = 0; i < data.levels.Count; i++ )
        {
            var level = data.levels[i];
            if ( string.IsNullOrEmpty( level.externalRelPath ) )
                continue;

            var levelPath = Path.Combine( _file.DirectoryName, level.externalRelPath );
            if ( !File.Exists( levelPath ) )
                continue;

            await using var levelFile = File.OpenRead( levelPath );
            level.externalLevel = await JsonSerializer.DeserializeAsync<LDTKProject.Level>( levelFile );
        }

        return json;
    }

    public async Task Save( FileInfo? _savePath = null )
    {
        _savePath ??= ProjectPath;

        identifierStyle = "Free";

        var originalContent = JsonDocument.Parse( await File.ReadAllTextAsync( ProjectPath.FullName ) );
        var mergedJson = JsonUtilities.Merge( originalContent, JsonSerializer.SerializeToDocument( this ) );

        var savePath = $"{ProjectPath.DirectoryName}\\{Path.GetFileNameWithoutExtension( _savePath.FullName )}_debug.ldtk";
        await File.WriteAllTextAsync( savePath, mergedJson );

        await using var metaFile = File.Open(Path.ChangeExtension(savePath, ".meta"), FileMode.Create);
        await JsonSerializer.SerializeAsync(metaFile, Meta, new JsonSerializerOptions() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault });
    }

    public sealed class MetaData
    {
        public Dictionary<string, ObjectInfo> Objects { get; set; } = new();

        public ObjectInfo? ObjectGet( string _name )
        {
            return Objects.GetValueOrDefault(_name.ToLower());
        }

        public void ObjectAdd( string _name, ObjectInfo _info )
        {
            Objects.Add(_name.ToLower(), _info);
        }

        public class ObjectInfo
        {
            public int uid { get; set; }
            public Dictionary<string, PropertyInfo> Properties { get; set; } = new();

            public class PropertyInfo
            {
                public int uid { get; set; }
                public string? type { get; set; }
                public bool gotError { get; set; }
            }
        }

    }

    public class Definitions
    {
        public List<object> layers { get; set; }
        public List<Entity> entities { get; set; }
        public List<Tileset> tilesets { get; set; }
        public List<Enum> enums { get; set; }
        public List<object> externalEnums { get; set; }
        public List<object> levelFields { get; set; }
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
        public string externalRelPath { get; set; }
        public List<object> fieldInstances { get; set; }
        public List<Layer> layerInstances { get; set; }
        public List<Neighbour> __neighbours { get; set; }

        [JsonIgnore]
        public Level? externalLevel { get; set; }

        public class Layer
        {
            public string __identifier { get; set; }
            public string __type { get; set; }
            public int __cWid { get; set; }
            public int __cHei { get; set; }
            public int __gridSize { get; set; }
            public int __opacity { get; set; }
            public int __pxTotalOffsetX { get; set; }
            public int __pxTotalOffsetY { get; set; }
            public int? __tilesetDefUid { get; set; }
            public string __tilesetRelPath { get; set; }
            public string iid { get; set; }
            public int levelId { get; set; }
            public int layerDefUid { get; set; }
            public int pxOffsetX { get; set; }
            public int pxOffsetY { get; set; }
            public bool visible { get; set; }
            public List<object> optionalRules { get; set; }
            public List<int> intGridCsv { get; set; }
            public List<AutoLayerTile> autoLayerTiles { get; set; }
            public int seed { get; set; }
            public object overrideTilesetUid { get; set; }
            public List<GridTile> gridTiles { get; set; }
            public List<EntityInstance> entityInstances { get; set; }

            public class EntityInstance
            {
                public string __identifier { get; set; }
                public List<int> __grid { get; set; }
                public List<double> __pivot { get; set; }
                public List<string> __tags { get; set; }
                public Tile __tile { get; set; }
                public string __smartColor { get; set; }
                public string iid { get; set; }
                public int width { get; set; }
                public int height { get; set; }
                public int defUid { get; set; }
                public List<int> px { get; set; }
                public List<FieldInstance> fieldInstances { get; set; }

                public class FieldInstance
                {
                    public string __identifier { get; set; }
                    public string __type { get; set; }
                    public object __value { get; set; }
                    public object? __tile { get; set; }
                    public int defUid { get; set; }
                    public List<DefaultOverride> realEditorValues { get; set; }
                }

                public class Tile
                {
                    public int tilesetUid { get; set; }
                    public int x { get; set; }
                    public int y { get; set; }
                    public int w { get; set; }
                    public int h { get; set; }
                }
            }

            public class AutoLayerTile
            {
                public List<int> px { get; set; }
                public List<int> src { get; set; }
                public int f { get; set; }
                public int t { get; set; }
                public List<int> d { get; set; }
                public int a { get; set; }
            }

            public class GridTile
            {
                public List<int> px { get; set; }
                public List<int> src { get; set; }
                public int f { get; set; }
                public int t { get; set; }
                public List<int> d { get; set; }
                public int a { get; set; }
            }
        }

        public class Neighbour
        {
            public string levelIid { get; set; }
            public string dir { get; set; }
        }
    }

    public class Entity
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
        public List<object> nineSliceBorders { get; set; } = new List<object>();
        public int maxCount { get; set; }
        public string limitScope { get; set; } = "PerLevel";
        public string limitBehavior { get; set; } = "MoveLastOne";
        public float pivotX { get; set; } = 0.5f;
        public float pivotY { get; set; } = 0.5f;
        public List<FieldDef> fieldDefs { get; set; } = new List<FieldDef>();

        public class FieldDef
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


        [JsonPropertyName("params")] 
        public List<object?> values { get; set; } = new List<object?>();

        [JsonIgnore]
        public IdTypes Type
        {
            get => System.Enum.Parse<IdTypes>(id);
            set => id = value.ToString();
        }

        public DefaultOverride() { }

        public DefaultOverride(IdTypes _type, params object[] _values)
        {
            Type = _type;
            values = new List<object>( _values );
        }

        public bool Equals(DefaultOverride? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            if (id != other.id)
                return false;

            if ( values.Count != other.values.Count)
                return false;

            for (int i = values.Count - 1; i >= 0; i--)
            {
                if (!ValueEquals(values[i], other.values[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((DefaultOverride)obj);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(id, values);
        }

        public override string ToString()
        {
            return $"{string.Join(',', values )} [{id}]";
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

    public class Tileset
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
        if (_left is null && _right is null)
            return true;
        if (_left is null || _right is null)
            return false;

        string leftString = GetComparisonValue(_left);
        string rightString = GetComparisonValue(_right);

        return leftString == rightString;
    }

    private static string GetComparisonValue( object _value )
    {
        switch (_value)
        {
            case JsonElement json:
                return json.GetRawText();
            default:
                return JsonSerializer.Serialize(_value);
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
                    .Select( o => Json2Object(o) )
                    .ToArray();
            default:
                return _element;
        }
    }
}