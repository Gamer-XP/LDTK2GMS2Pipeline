using LDTK2GMS2Pipeline.LDTK;
using SixLabors.ImageSharp;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal static partial class GM2LDTK
{
    /// <summary>
    /// Updates sprite atlas used in LDTK
    /// </summary>
    /// <param name="_gmProject">Game Maker project</param>
    /// <param name="_ldtkProject">LDTK project</param>
    /// <param name="_levelObjects">Required GM objects</param>
    /// <param name="_forceUpdateAtlas">Forces atlas update even if nothing changed</param>
    private static async Task<(SpriteAtlas atlas, IEnumerable<GMSprite> sprites, LDTKProject.Tileset atlasTileset )>
        UpdateAtlas(GMProject _gmProject, LDTKProject _ldtkProject, List<GMObject> _levelObjects, bool _forceUpdateAtlas)
    {
        string atlasPath = Path.Combine(_ldtkProject.ProjectPath.DirectoryName!, SharedData.IconFolder, $"{SharedData.EntityAtlasName}.png");
        SpriteAtlas atlas = new(atlasPath, _ldtkProject.defaultGridSize);

        var sprites = GetRequiredSprites(_levelObjects, _ldtkProject);
        foreach (var spriteInfo in sprites)
            atlas.Add(spriteInfo.sprite, spriteInfo.allFrames);

        _forceUpdateAtlas |= atlas.IsNew;

        bool atlasUpdated = await atlas.Update();

        LDTKProject.Tileset tileset = GetAtlasTileset(_ldtkProject, atlasUpdated, atlas);

        if (atlasUpdated || _forceUpdateAtlas)
            UpdateAtlasReferences(_ldtkProject, _gmProject, atlas, tileset, _forceUpdateAtlas);

        return (atlas, sprites.Where(t => t.allFrames).Select(t => t.sprite), tileset);
    }

    private static LDTKProject.Tileset GetAtlasTileset(LDTKProject _ldtkProject, bool _atlasUpdated, SpriteAtlas _atlas)
    {
        _atlasUpdated |= _ldtkProject.CreateOrExistingForced(SharedData.EntityAtlasName, out LDTKProject.Tileset tileset);

        if (!_atlasUpdated)
            return tileset;

        tileset.relPath = $"{SharedData.IconFolder}/{SharedData.EntityAtlasName}.png";
        tileset.pxWid = _atlas.Width;
        tileset.pxHei = _atlas.Height;
        tileset.tileGridSize = 16;
        tileset.__cWid = tileset.pxWid / tileset.tileGridSize;
        tileset.__cHei = tileset.pxHei / tileset.tileGridSize;

        return tileset;
    }

    private static void UpdateAtlasReferences(LDTKProject _ldtkProject, GMProject _gmProject, SpriteAtlas _atlas, LDTKProject.Tileset _atlasTileset, bool _forceUpdate = false)
    {
        foreach (LDTKProject.Entity entity in _ldtkProject.defs.entities)
        {
            if (entity.tilesetId != _atlasTileset.uid || entity.tileRect == null)
                continue;

            GMObject? obj = _gmProject.FindResourceByName(entity.Meta?.identifier, typeof(GMObject)) as GMObject;
            GMSprite? objectSprite = obj?.spriteId;
            if (obj == null || objectSprite == null)
                continue;

            bool isSoftUpdate = false;

            dynamic rect;

            if (!_forceUpdate)
            {
                var currentRect = new Rectangle(entity.tileRect.x, entity.tileRect.y, entity.tileRect.w,
                    entity.tileRect.h);

                Rectangle? newRect = _atlas.UpdatePosition(currentRect, objectSprite);
                if (newRect != null)
                {
                    rect = newRect.Value;
                    isSoftUpdate = true;
                    goto found;
                }
            }

            var spriteName = objectSprite.name;
            SpriteAtlas.IAtlasItem? atlasItem = _atlas.Get(spriteName);
            if (atlasItem == null)
                continue;

            rect = atlasItem.Rectangle;

            found:

            entity.tileRect = new LDTKProject.TileRect()
            {
                tilesetUid = _atlasTileset.uid,
                x = rect.X,
                y = rect.Y,
                w = rect.Width,
                h = rect.Height
            };

            if (!isSoftUpdate)
            {
                entity.InitSprite(obj, _atlasTileset, _atlas);
            }
        }
    }

    /// <summary>
    /// Returns all sprites required to be imported to LDTK.
    /// Generated based on list of required objects.
    /// </summary>
    private static List<(GMSprite sprite, bool allFrames)> GetRequiredSprites( List<GMObject> _objects, LDTKProject _ldtkProject )
    {
        Dictionary<GMSprite, bool> checker = new Dictionary<GMSprite, bool>();

        foreach (GMObject obj in _objects.Where( t => t.spriteId != null))
        {
            if (checker.TryGetValue(obj.spriteId, out bool allFrames))
            {
                if (allFrames)
                    continue;
            }

            allFrames = false;

            var res = _ldtkProject.GetResource<LDTKProject.Entity>(obj.name);
            if (res != null)
            {
                allFrames = res.fieldDefs.Exists(t => t.Meta?.identifier == SharedData.ImageIndexState || t.identifier == SharedData.ImageIndexState );
            }

            checker[obj.spriteId] = allFrames;
        }

        return checker.Select( pair => (pair.Key, pair.Value)).ToList();
    }
}