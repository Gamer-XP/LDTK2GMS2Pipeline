using LDTK2GMS2Pipeline.LDTK;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal static partial class GM2LDTK
{
    /// <summary>
    /// Updates auto-generated enums
    /// </summary>
    private static void UpdateEnums(LDTKProject _ldtkProject, GMProject _gmProject, IEnumerable<GMSprite> _spriteEnums, SpriteAtlas _atlas, LDTKProject.Tileset _atlasTileset)
    {
        void UpdateEnum(string _name, IEnumerable<string> _values, bool _sort = true)
        {
            _ldtkProject.CreateOrExisting(_name, out LDTKProject.Enum? en);
            if (en != null)
                en.UpdateValues(_sort ? _values.OrderBy(t => t) : _values);
        }

        UpdateEnum("AUTO_LAYERS", _ldtkProject.defs.layers.Select(t => t.identifier), false);

        UpdateEnum("GM_OBJECTS", _gmProject.GetResourcesByType<GMObject>().Select(t => t.name));

        UpdateEnum("GM_ROOMS", _gmProject.GetResourcesByType<GMRoom>().Select(t => t.name));

        UpdateEnum("GM_SOUNDS", _gmProject.GetResourcesByType<GMSound>().Select(t => t.name));

        foreach (GMSprite sprite in _spriteEnums)
        {
            _ldtkProject.CreateOrExistingForced(sprite.name, out LDTKProject.Enum en);

            if (sprite.frames.Count != en.values.Count)
            {
                if (sprite.frames.Count > en.values.Count)
                {
                    for (int i = en.values.Count; i < sprite.frames.Count; i++)
                    {
                        en.values.Add(new LDTKProject.Enum.Value() { id = $"Image_{i}" });
                    }
                }
                else
                {
                    for (int i = en.values.Count - 1; i >= sprite.frames.Count; i--)
                    {
                        en.values.RemoveAt(i);
                    }
                }
            }

            en.iconTilesetUid = _atlasTileset.uid;
            for (int i = 0; i < en.values.Count; i++)
            {
                var item = _atlas.Get(sprite, i);
                if (item == null)
                    continue;

                en.values[i].tileRect = new LDTKProject.TileRect()
                {
                    tilesetUid = _atlasTileset.uid,
                    x = item.Rectangle.X,
                    y = item.Rectangle.Y,
                    w = item.Rectangle.Width,
                    h = item.Rectangle.Height
                };
            }
        }
    }
}