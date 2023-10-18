using System.Collections;
using System.Text.Json.Serialization;

namespace LDTK2GMS2Pipeline.LDTK;

public sealed class LDTKMetaData
{
    [JsonIgnore]
    public LDTKProject Project { get; internal set; }

    [JsonInclude]
    public List<LDTKProject.Entity.MetaData> objects { get; private set; } = new();
    [JsonInclude]
    public List<LDTKProject.Tileset.MetaData> tilesets { get; private set; } = new();
    [JsonInclude]
    public List<LDTKProject.Level.MetaData> levels { get; private set; } = new();
    [JsonInclude]
    public List<LDTKProject.Enum.MetaData> enums { get; private set; } = new();
    [JsonInclude]
    public List<LDTKProject.Layer.MetaData> layers { get; private set; } = new();

    private Dictionary<Type, IList>? dictCache = null;

    private Dictionary<Type, IList> GetCache()
    {
        return dictCache ??= new Dictionary<Type, IList>
        {
            { typeof(LDTKProject.Entity.MetaData), objects },
            { typeof(LDTKProject.Tileset.MetaData), tilesets },
            { typeof(LDTKProject.Level.MetaData), levels },
            { typeof(LDTKProject.Enum.MetaData), enums },
            { typeof(LDTKProject.Layer.MetaData), layers }
        };
    }

    public IList GetList( Type _metaType )
    {
        return GetCache()[_metaType];
    }
}