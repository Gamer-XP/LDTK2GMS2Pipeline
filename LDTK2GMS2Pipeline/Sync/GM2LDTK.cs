using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Utilities;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal static partial class GM2LDTK
{
    public static async Task<SpriteAtlas> ImportToLDTK( GMProject _gmProject, LDTKProject _ldtkProject, bool _forceUpdateAtlas )
    {
        List<GMObject> levelObjects = GetRequiredObjects(_gmProject, _ldtkProject);

        Log.PushTitle("ATLAS");
        var atlasInfo = await UpdateAtlas(_gmProject, _ldtkProject, levelObjects, _forceUpdateAtlas);
        Log.PopTitle();
        
        Log.PushTitle("ENUMS");
        UpdateEnums( _ldtkProject, _gmProject, atlasInfo.sprites, atlasInfo.atlas, atlasInfo.atlasTileset );
        Log.PopTitle();
        
        Log.PushTitle("ENTITIES");
        UpdateEntities( levelObjects, _ldtkProject, atlasInfo.atlas, atlasInfo.atlasTileset );
        Log.PopTitle();

        Log.PushTitle("TILESETS");
        UpdateTilesets( _ldtkProject, _gmProject.GetResourcesByType<GMTileSet>().Cast<GMTileSet>().ToList() );
        Log.PopTitle();
        
        Log.PushTitle("LEVELS");
        UpdateLevels( _gmProject, _ldtkProject, _gmProject.GetResourcesByType<GMRoom>().Cast<GMRoom>().ToList() );
        Log.PopTitle();

        return atlasInfo.atlas;
    }
    
    /// <summary>
    /// Returns list of objects to be imported to LDTK.
    /// Filtered by tag on the objects
    /// </summary>
    private static List<GMObject> GetRequiredObjects( GMProject _project, LDTKProject _ldtkProject )
    {
        string tag = _ldtkProject.Options.LevelObjectTag;
        
        return _project
            .GetResourcesByType<GMObject>()
            .Cast<GMObject>()
            .Where(t => t.tags.Contains(tag))
            .ToList();
    }
}
