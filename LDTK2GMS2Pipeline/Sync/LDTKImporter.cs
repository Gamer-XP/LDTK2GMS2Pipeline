using LDTK2GMS2Pipeline.LDTK;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject.Layer;
using Rule = Spectre.Console.Rule;

namespace LDTK2GMS2Pipeline.Sync;

internal static class LDTKImporter
{
    public static async Task ImportToLDTK(GMProject _gmProject, LDTKProject _ldtkProject)
    {
        var levelObjects = _gmProject
            .GetResourcesByType<GMObject>()
            .Cast<GMObject>()
            .Where(t => t.tags.Contains( SharedData.LevelObjectTag ) )
            .ToList();

        var sprites = levelObjects
            .Where(t => t.spriteId != null)
            .Select(t => t.spriteId!)
            .Distinct()
            .ToList();

        AnsiConsole.Write(new Rule("ATLAS"));

        string atlasPath = Path.Combine(_ldtkProject.ProjectPath.DirectoryName!, SharedData.IconFolder, $"{SharedData.EntityAtlasName}.png");
        SpriteAtlas atlas = new(atlasPath, _ldtkProject.defaultGridSize);
        atlas.Add(sprites);

        bool atlasUpdated = await atlas.Update();

        Tileset tileset = GetAtlasTileset(_ldtkProject, atlasUpdated, atlas);

        if (atlasUpdated)
            UpdateAtlasReferences(_ldtkProject, atlas, tileset);

        AnsiConsole.Write(new Rule("ENTITIES"));

        UpdateEntities(levelObjects, _ldtkProject, atlas, tileset);

        AnsiConsole.Write(new Rule("TILESETS"));

        await UpdateTilesets(_ldtkProject, _gmProject.GetResourcesByType<GMTileSet>().Cast<GMTileSet>().ToList());

        AnsiConsole.Write(new Rule("LEVELS"));

        UpdateLevels(_gmProject, _ldtkProject, _gmProject.GetResourcesByType<GMRoom>().Cast<GMRoom>().ToList());
    }

    private static async Task UpdateTilesets(LDTKProject _project, List<GMTileSet> _tilesets)
    {
        foreach (GMTileSet gmTileset in _tilesets)
        {
            _project.CreateOrExisting<Tileset>(gmTileset.name, out var tileset);
            if (tileset == null)
                continue;

            if (gmTileset.tilehsep != gmTileset.tilevsep)
                AnsiConsole.MarkupLineInterpolated($"Error in [teal]{gmTileset.name}[/]! [red]Different spacing is not supported by LDTK: {gmTileset.tilehsep}, {gmTileset.tilevsep}[/]");

            if (gmTileset.tilexoff != gmTileset.tileyoff)
                AnsiConsole.MarkupLineInterpolated($"Error in [teal]{gmTileset.name}[/]! [red]Different offsets are not supported by LDTK: {gmTileset.tilexoff}, {gmTileset.tileyoff}[/]");

            if (gmTileset.tilexoff != 0 || gmTileset.tileyoff != 0)
                AnsiConsole.MarkupLineInterpolated($"Warning in [teal]{gmTileset.name}[/]! [yellow]Offsets work as padding in LDTK. You may lose most right and bottom tiles![/]");

            tileset.pxWid = gmTileset.spriteId.width;
            tileset.pxHei = gmTileset.spriteId.height;
            tileset.tileGridSize = gmTileset.tileWidth;
            // LDTK treats partially-filled cells as proper tiles, while GM ignores them
            tileset.__cWid = (gmTileset.spriteId.width - gmTileset.tilexoff + gmTileset.tilehsep + gmTileset.tileWidth + gmTileset.tilehsep - 2) / (gmTileset.tileWidth + gmTileset.tilehsep);
            tileset.__cHei = (gmTileset.spriteId.height - gmTileset.tileyoff + gmTileset.tilevsep + gmTileset.tileHeight + gmTileset.tilevsep - 2) / (gmTileset.tileHeight + gmTileset.tilevsep);
            tileset.spacing = gmTileset.tilehsep;
            tileset.padding = gmTileset.tilexoff;

            string tilesetFullPath = GMProjectUtilities.GetFullPath(gmTileset.spriteId.GetCompositePaths()[0]);
            tileset.relPath = Path.GetRelativePath(_project.ProjectDirectory, tilesetFullPath).Replace("\\", "/");
        }
    }

    private static void UpdateLevels(GMProject _gmProject, LDTKProject _project, List<GMRoom> _rooms)
    {
        var entityDict = _project.CreateResourceMap<GMObject, Entity>(_gmProject);
        var tilesetDict = _project.CreateResourceMap<GMTileSet, Tileset>(_gmProject);

        var flipEnum = SharedData.GetFlipEnum(_project);

        foreach (GMRoom room in _rooms)
        {
            //if ( room.name != "rm_combat_test" )
            //    continue;

            if (_project.CreateOrExisting<Level>(room.name, out var level))
                level.worldX = GetMaxX();
            else
            {
                IResourceContainerUtilities.EnableLogging = IResourceContainerUtilities.LoggingLevel.On;
            }

            if (level != null)
                UpdateLevel(room, level);

            IResourceContainerUtilities.EnableLogging = IResourceContainerUtilities.LoggingLevel.Auto;
        }

        int GetMaxX()
        {
            return _project.levels.Max(t => t.worldX + t.pxWid);
        }

        void UpdateLevel(GMRoom room, Level level)
        {
            level.pxWid = room.roomSettings.Width;
            level.pxHei = room.roomSettings.Height;

            List<GMRLayer>? gmLayers = room.AllLayers();
            foreach (Layer layerDef in _project.defs.layers)
            {
                if (level.CreateOrExistingForced<Level.Layer>(layerDef.identifier, out var layer))
                {
                    layer.__type = layerDef.__type;
                    layer.__gridSize = layerDef.gridSize;
                    layer.seed = Random.Shared.Next( 9999999 );
                    layer.__cWid = (room.roomSettings.Width + layerDef.gridSize - 1) / layerDef.gridSize;
                    layer.__cHei = (room.roomSettings.Height + layerDef.gridSize - 1) / layerDef.gridSize;
                    layer.levelId = level.uid;
                    layer.layerDefUid = layerDef.uid;
                }

                var gmLayer = gmLayers.Find(t => t.name == layerDef.identifier);
                if (gmLayer == null)
                    continue;

                if (!Layer.CanBeConverted(gmLayer, layerDef.__type))
                {
                    AnsiConsole.MarkupLineInterpolated( $"[yellow]Layer types do not match for [green]{layerDef.identifier}[/] in [olive]{room.name}[/]: {gmLayer.GetType().Name} vs {layerDef.__type}[/]" );
                    continue;
                }

                layer.visible = gmLayer.visible;

                switch (gmLayer)
                {
                    case GMRInstanceLayer instLayer:

                        layer.RemoveUnusedMeta<Level.EntityInstance.MetaData>(
                            instLayer.instances.Where(t => !t.ignore).Select(t => t.name),
                            _s =>
                            {
                                if (!layer.Remove<Level.EntityInstance>(_s.identifier))
                                    Console.WriteLine($"Unable to remove object of type {_s.Resource.identifier}");
                            });

                        foreach (GMRInstance gmInstance in instLayer.instances)
                        {
                            if (gmInstance.ignore || !entityDict.TryGetValue(gmInstance.objectId, out var entityType))
                            {
                                if (!gmInstance.ignore)
                                    AnsiConsole.MarkupLineInterpolated($"[yellow]Unable to find matching object [teal]{gmInstance.objectId.name}[/] for level [olive]{room.name}[/][/]");

                                layer.Remove<Level.EntityInstance>(gmInstance.name);
                                continue;
                            }

                            bool GetField(string _name, out Field.MetaData _meta)
                            {
                                if (_name == null)
                                {
                                    _meta = null;
                                    return false;
                                }
                                _meta = entityType.GetMeta<Field.MetaData>(_name);
                                return _meta?.Resource != null;
                            }

                            int width = (int)(entityType.width * MathF.Abs(gmInstance.scaleX));
                            int height = (int)(entityType.height * MathF.Abs(gmInstance.scaleY));
                            int posX = (int)gmInstance.x;
                            int posY = (int)gmInstance.y;

                            if (layer.CreateOrExistingForced(gmInstance.name, out Level.EntityInstance instance))
                            {
                                instance.__pivot = new List<double>() { entityType.pivotX, entityType.pivotY };
                                instance.__identifier = entityType.identifier;
                                instance.defUid = entityType.uid;
                                instance.__tile = entityType.tileRect;
                            }

                            if (gmInstance.flipX)
                                posX += (int)((entityType.pivotX - 0.5f) * 2f * width);
                            if (gmInstance.flipY)
                                posY += (int)((entityType.pivotY - 0.5f) * 2f * height);

                            instance.__tags = entityType.tags;
                            instance.px = new List<int>() { posX, posY };
                            instance.__grid = new List<int>() { posX / layerDef.gridSize, posY / layerDef.gridSize };
                            instance.__worldX = level.worldX + posX;
                            instance.__worldY = level.worldY + posY;
                            instance.width = width;
                            instance.height = height;

                            int flipIndex = (gmInstance.flipX ? 1 : 0) | (gmInstance.flipY ? 2 : 0);

                            if (flipIndex > 0)
                            {
                                if (GetField(SharedData.FlipStateEnumName, out Field.MetaData fieldMeta))
                                {
                                    instance.CreateOrExistingForced(fieldMeta.identifier, out Level.FieldInstance fi, fieldMeta.uid);

                                    fi.SetValue( DefaultOverride.IdTypes.V_String, flipEnum.values[flipIndex].id );
                                }
                            }

                            foreach (GMOverriddenProperty propOverride in gmInstance.properties)
                            {
                                if (!GetField(propOverride.varName, out Field.MetaData fieldMeta ) )
                                    continue;

                                if (!ConvertDefaultValue(propOverride.propertyId, propOverride.value, out var result, fieldMeta.type))
                                {
                                    AnsiConsole.MarkupLineInterpolated($"[red]Error processing value '{propOverride.value}' for field [green]{propOverride.varName} [[{propOverride.propertyId.varType}]][/] in [teal]{gmInstance.objectId.name}[/].[/]");
                                    instance.CreateMetaFor<Level.FieldInstance.MetaData>( fieldMeta.identifier, fieldMeta.Resource!.uid).GotError = true;
                                    continue;
                                }

                                instance.CreateOrExistingForced(fieldMeta.identifier, out Level.FieldInstance fi, fieldMeta.uid);

                                fi.Meta.GotError = false;
                                fi.SetValue(result);
                            }
                        }

                        break;

                    case GMRTileLayer tileLayer:

                        Tileset? tileset = tileLayer.tilesetId != null ? tilesetDict.GetValueOrDefault(tileLayer.tilesetId) : null;

                        if (tileset == null)
                        {
                            layer.__tilesetRelPath = null;
                            layer.__tilesetDefUid = null;
                            break;
                        }

                        var gmTileset = tileLayer.tilesetId;

                        int gridSizeWithSpacing = tileset.tileGridSize + tileset.spacing;

                        layer.__tilesetRelPath = tileset.relPath;
                        layer.__tilesetDefUid = tileset.uid;
                        layer.overrideTilesetUid = tileset.uid;

                        layer.pxOffsetX = tileLayer.x;
                        layer.pxOffsetY = tileLayer.y;

                        var tilemap = tileLayer.tiles;
                        uint[,] tiles = tilemap.Tiles;

                        var gmTilesetWidth = (gmTileset.spriteId.width - gmTileset.tilexoff + gmTileset.tilehsep) / (gmTileset.tileWidth + gmTileset.tilehsep);

                        layer.gridTiles = new List<Level.Layer.TileInstance>(tilemap.Width * tilemap.Height);

                        for (int roomY = 0; roomY < tilemap.Height; roomY++)
                            for (int roomX = 0; roomX < tilemap.Width; roomX++)
                            {
                                var value = tiles[roomX, roomY];

                                uint index = value & TileMap.TileBitMask_TileIndex;
                                if (index == 0U)
                                    continue;

                                bool flipX = TileMap.CheckBits(value, TileMap.TileBitMask_Flip);
                                bool flipY = TileMap.CheckBits(value, TileMap.TileBitMask_Mirror);
                                int tilesetX = (int)(index % gmTilesetWidth);
                                int tilesetY = (int)(index / gmTilesetWidth);
                                var tile = new Level.Layer.TileInstance();
                                tile.t = tilesetX + tilesetY * tileset.__cWid;
                                tile.f = (flipX ? 2 : 0) | (flipY ? 1 : 0);
                                tile.px = new int[] { roomX * tileset.tileGridSize, roomY * tileset.tileGridSize };
                                tile.src = new int[] { tileset.padding + tilesetX * gridSizeWithSpacing, tileset.padding + tilesetY * gridSizeWithSpacing };
                                tile.d = new int[] { roomX + roomY * tilemap.Width };

                                layer.gridTiles.Add(tile);
                            }


                        break;
                }

            }
        }
    }

    private static Tileset GetAtlasTileset(LDTKProject _ldtkProject, bool _atlasUpdated, SpriteAtlas _atlas)
    {
        _atlasUpdated |= _ldtkProject.CreateOrExistingForced(SharedData.EntityAtlasName, out Tileset tileset);

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

    private static void UpdateAtlasReferences(LDTKProject _ldtkProject, SpriteAtlas _atlas, Tileset _atlasTileset)
    {
        foreach (Entity entity in _ldtkProject.defs.entities)
        {
            if (entity.tilesetId != _atlasTileset.uid || entity.tileRect == null)
                continue;

            var currentRect = new Rectangle(entity.tileRect.x, entity.tileRect.y, entity.tileRect.w, entity.tileRect.h);

            Rectangle? newRect = _atlas.UpdatePosition(currentRect);
            if (newRect == null)
                continue;

            var rect = newRect.Value;
            entity.tileRect = new TileRect()
            {
                tilesetUid = _atlasTileset.uid,
                x = rect.X,
                y = rect.Y,
                w = rect.Width,
                h = rect.Height
            };
        }
    }

    private static void UpdateEntities(List<GMObject> _objects, LDTKProject _ldtkProject, SpriteAtlas _atlas, Tileset _atlasTileset)
    {
        var sortedList = _objects.OrderBy(t => t.name).ToList();
        sortedList.Sort((l, r) =>
        {
            if (GMProjectUtilities.IsInheritedFrom(l, r))
                return 1;
            if (GMProjectUtilities.IsInheritedFrom(r, l))
                return -1;
            return 0;
        });

        foreach (var levelObject in _objects.OrderBy(t => t.name))
        {
            bool isNew = _ldtkProject.CreateOrExisting(levelObject.name, out Entity? entity);

            if (entity != null)
            {
                if (isNew)
                    entity.Init(levelObject, _atlasTileset, _atlas);
                IResourceContainerUtilities.EnableLogging = isNew
                    ? IResourceContainerUtilities.LoggingLevel.Auto
                    : IResourceContainerUtilities.LoggingLevel.On;
                _ldtkProject.UpdateEntity(entity, levelObject);
                IResourceContainerUtilities.EnableLogging = IResourceContainerUtilities.LoggingLevel.Auto;
            }
        }

        _ldtkProject.RemoveUnusedMeta<Entity.MetaData>(_objects.Select(t => t.name), _s =>
        {
            AnsiConsole.MarkupLineInterpolated($"Object [teal]{_s}[/] no longer exists in the GM project. Removing from meta...");
        });
    }
}
