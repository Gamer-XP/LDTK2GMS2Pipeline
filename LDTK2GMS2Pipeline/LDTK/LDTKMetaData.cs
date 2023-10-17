using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.LDTK;

public sealed class LDTKMetaData
{
    [JsonIgnore]
    public LDTKProject Project { get; internal set; }

    public Dictionary<string, LDTKProject.Entity.MetaData> objects { get; set; } = new();
    public Dictionary<string, LDTKProject.Tileset.MetaData> tilesets { get; set; } = new();
    public Dictionary<string, LDTKProject.Level.MetaData> levels { get; set; } = new();
    public Dictionary<string, LDTKProject.Enum.MetaData> enums { get; set; } = new();
    
    private Dictionary<Type, IDictionary>? dictCache = null;

    private Dictionary<Type, IDictionary> GetCache()
    {
        return dictCache ??= new Dictionary<Type, IDictionary>
        {
            { typeof(LDTKProject.Entity.MetaData), objects },
            { typeof(LDTKProject.Tileset.MetaData), tilesets },
            { typeof(LDTKProject.Level.MetaData), levels },
            { typeof(LDTKProject.Enum.MetaData), enums }
        };
    }

    public IDictionary GetDictionary(Type _metaType)
    {
        return GetCache()[_metaType];
    }
}