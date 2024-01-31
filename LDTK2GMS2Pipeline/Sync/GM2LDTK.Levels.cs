using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Utilities;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal static partial class GM2LDTK
{
    private static void UpdateLevels(GMProject _gmProject, LDTKProject _project, List<GMRoom> _rooms)
    {
        var entityDict = _project.CreateResourceMap<GMObject, LDTKProject.Entity>(_gmProject);
        var tilesetDict = _project.CreateResourceMap<GMTileSet, LDTKProject.Tileset>(_gmProject);

        var flipEnum = SharedData.GetFlipEnum(_project);

        var entityGM2LDTK = MatchEntities();

        //Log.EnableAutoLog = Log.AutoLogLevel.Off;

        foreach (var pair in _project.EnumeratePairedResources<LDTKProject.Level.MetaData, GMRoom>(_rooms))
        {
            if (pair.res == null && pair.meta != null)
            {
                _project.Remove<LDTKProject.Level>(pair.meta.identifier);
            }
        }

        foreach (GMRoom room in _rooms)
        {
            if (_project.CreateOrExisting<LDTKProject.Level>(room.name, out var level))
            {
                level!.worldX = GetMaxX();
            }
            else if (level != null)
            {
                Log.PushResource(level);
            }

            if (level != null)
            {
                UpdateLevel(room, level);
                Log.Pop();
            }
        }

        //Log.EnableAutoLog = Log.AutoLogLevel.Auto;

        int GetMaxX()
        {
            return _project.levels.Max(t => t.worldX + t.pxWid);
        }

        Dictionary<string, string> MatchEntities()
        {
            Dictionary<string, string> result = new();

            // Finding already existing instances
            foreach (var instance in _project.levels.SelectMany(t => t.layerInstances).SelectMany(t => t.entityInstances))
            {
                if (instance.Meta == null)
                    continue;

                result.Add(instance.Meta.identifier, instance.iid);
            }

            // Generating iids for missing instances
            foreach (var layerInstance in _rooms
                         .SelectMany(r => r.layers)
                         .Where(l => l is GMRInstanceLayer)
                         .Cast<GMRInstanceLayer>()
                         .SelectMany(l => l.instances))
            {
                if (result.ContainsKey(layerInstance.name))
                    continue;

                result.Add(layerInstance.name, Guid.NewGuid().ToString());
            }

            return result;
        }

        void TryLoadDepth(LDTKProject.Level _level, LDTKProject.Level.Layer _layer, GMRLayer _gmLayer)
        {
            var depthField = _level.GetLayerDepthField(_layer);
            if (depthField == null)
                return;

            var fieldDef = _project.defs.levelFields.Find(t => t.uid == depthField.defUid);
            if (fieldDef == null)
                return;

            int defaultDepth = 0;
            if (fieldDef.defaultOverride != null && !fieldDef.defaultOverride.TryGet(out defaultDepth))
                return;

            depthField.__value = _gmLayer.depth;

            bool isOverridden = _gmLayer.depth != defaultDepth;
            if (isOverridden)
                depthField.realEditorValues = new() { new LDTKProject.DefaultOverride(LDTKProject.DefaultOverride.IdTypes.V_Int, _gmLayer.depth) };
            else
                depthField.realEditorValues = new(0);
        }

        void UpdateLevel(GMRoom _room, LDTKProject.Level _level)
        {
            _level.pxWid = _room.roomSettings.Width;
            _level.pxHei = _room.roomSettings.Height;

            List<GMRLayer>? gmLayers = _room.AllLayers();
            foreach (LDTKProject.Layer layerDef in _project.defs.layers)
            {
                if (_level.CreateOrExistingForced<LDTKProject.Level.Layer>(layerDef.identifier, out var layer))
                {
                    layer.__type = layerDef.__type;
                    layer.__gridSize = layerDef.gridSize;
                    layer.seed = Random.Shared.Next(9999999);
                    layer.levelId = _level.uid;
                    layer.layerDefUid = layerDef.uid;
                }
                else
                {
                    Log.PushResource(layerDef);
                }

                using var layerStack = Log.PopOnDispose();

                layer.__cWid = (_room.roomSettings.Width + layerDef.gridSize - 1) / layerDef.gridSize;
                layer.__cHei = (_room.roomSettings.Height + layerDef.gridSize - 1) / layerDef.gridSize;

                var gmLayer = gmLayers.Find(t => t.name == layerDef.identifier);
                if (gmLayer == null)
                {
                    // Cleanup layer if there is no matching one in GM
                    layer.autoLayerTiles.Clear();
                    layer.entityInstances.Clear();
                    layer.gridTiles.Clear();
                    layer.visible = false;
                    continue;
                }

                if (!LDTKProject.Layer.CanBeConverted(gmLayer, layerDef.__type))
                {
                    Log.Write($"[{Log.ColorWarning}]Layer types do not match for [{Log.ColorLayer}]{layerDef.identifier}[/]: {gmLayer.GetType().Name} vs {layerDef.__type}[/]");
                    continue;
                }

                TryLoadDepth(_level, layer, gmLayer);

                layer.visible = gmLayer.visible;

                switch (gmLayer)
                {
                    case GMRInstanceLayer instLayer:

                        void LogInstanceDeletion(string _mainName, string? _subName = null)
                        {
                            if (_subName != null)
                                Log.Write($"[{Log.ColorDeleted}]EntityInstance [{Log.ColorEntity}]{_mainName}[/] [[{_subName}]] removed.[/]");
                            else
                                Log.Write($"[{Log.ColorDeleted}]EntityInstance [{Log.ColorEntity}]{_mainName}[/] removed.[/]");
                        }

                        layer.RemoveUnusedMeta<LDTKProject.Level.EntityInstance.MetaData>(
                            instLayer.instances.Where(t => !t.ignore).Select(t => t.name),
                            _s =>
                            {
                                layer.Remove<LDTKProject.Level.EntityInstance>(_s.identifier);
                                
                                if (_s.Resource != null)
                                    LogInstanceDeletion(_s.Resource.identifier, _s.identifier);
                                else
                                    LogInstanceDeletion(_s.identifier);
                            });

                        layer.RemoveUnknownResources<LDTKProject.Level.EntityInstance>((r) => { LogInstanceDeletion(r.__identifier); });

                        foreach (GMRInstance gmInstance in instLayer.instances)
                        {
                            if (gmInstance.ignore || gmInstance.objectId == null || !entityDict.TryGetValue(gmInstance.objectId, out var entityType))
                            {
                                if (!gmInstance.ignore && gmInstance.objectId != null)
                                {
                                    Log.Write($"[{Log.ColorWarning}]Unable to find matching object [{Log.ColorEntity}]{gmInstance.objectId.name}[/][/]");
                                }

                                if (layer.Remove<LDTKProject.Level.EntityInstance>(gmInstance.name))
                                    LogInstanceDeletion(gmInstance.objectId?.name ?? "???", gmInstance.name);
                                continue;
                            }

                            List<string> usedProperties = new List<string>();

                            bool GetField(string _name, out LDTKProject.Field.MetaData _meta)
                            {
                                if (_name == null)
                                {
                                    _meta = null;
                                    return false;
                                }

                                _meta = entityType.GetMeta<LDTKProject.Field.MetaData>(_name);
                                if (_meta?.Resource == null)
                                    return false;

                                usedProperties.Add(_name);

                                return true;
                            }

                            int width = (int)(entityType.width * MathF.Abs(gmInstance.scaleX));
                            int height = (int)(entityType.height * MathF.Abs(gmInstance.scaleY));
                            int posX = (int)gmInstance.x;
                            int posY = (int)gmInstance.y;
                            
                            if (layer.CreateOrExistingForced(gmInstance.name, out LDTKProject.Level.EntityInstance instance, entityGM2LDTK.GetValueOrDefault(gmInstance.name)))
                            {
                                instance.__pivot = new List<double>() { entityType.pivotX, entityType.pivotY };
                                instance.__identifier = entityType.identifier;
                                instance.defUid = entityType.uid;
                                instance.__tile = entityType.tileRect;

                                Log.Push($"[{Log.ColorCreated}]EntityInstance [{Log.ColorEntity}]{gmInstance.objectId.name}[/] [[{gmInstance.name}]] created[/]", false);
                            }
                            else
                                Log.Push($"EntityInstance [{Log.ColorEntity}]{gmInstance.objectId.name}[/] [[{gmInstance.name}]]");

                            using var instanceStack = Log.PopOnDispose();

                            if (gmInstance.flipX)
                                posX += (int)((entityType.pivotX - 0.5f) * 2f * width);
                            if (gmInstance.flipY)
                                posY += (int)((entityType.pivotY - 0.5f) * 2f * height);

                            instance.__tags = entityType.tags;
                            instance.px = new List<int>() { posX, posY };
                            instance.__grid = new List<int>() { posX / layerDef.gridSize, posY / layerDef.gridSize };
                            instance.__worldX = _level.worldX + posX;
                            instance.__worldY = _level.worldY + posY;
                            instance.width = width;
                            instance.height = height;

                            int flipIndex = (gmInstance.flipX ? 1 : 0) | (gmInstance.flipY ? 2 : 0);

                            if (flipIndex > 0)
                            {
                                if (GetField(SharedData.FlipStateEnumName, out LDTKProject.Field.MetaData fieldMeta))
                                {
                                    instance.CreateOrExistingForced(fieldMeta.identifier, out LDTKProject.Level.FieldInstance fi, fieldMeta.uid);

                                    fi.SetValue(LDTKProject.DefaultOverride.IdTypes.V_String, flipEnum.values[flipIndex].id);
                                }
                            }

                            if (gmInstance.imageIndex > 0)
                            {
                                if (GetField(SharedData.ImageIndexState, out LDTKProject.Field.MetaData fieldMeta))
                                {
                                    instance.CreateOrExistingForced(fieldMeta.identifier, out LDTKProject.Level.FieldInstance fi, fieldMeta.uid);

                                    if (!FieldConversion.GM2LDTK(_project, gmInstance.imageIndex.ToString(), fieldMeta.Resource!.__type, SharedData.ImageIndexProperty, out var result))
                                    {
                                        continue;
                                    }

                                    fi.SetValues(result);
                                }
                            }

                            foreach (GMOverriddenProperty propOverride in gmInstance.properties)
                            {
                                if (!GetField(propOverride.varName, out LDTKProject.Field.MetaData fieldMeta) || _project.Options.IsPropertyIgnored(propOverride.objectId, propOverride.propertyId, gmInstance.objectId))
                                {
                                    instance.Remove<LDTKProject.Level.FieldInstance>(propOverride.varName, true);
                                    continue;
                                }

                                if (!FieldConversion.GM2LDTK(_project, propOverride.value, fieldMeta.Resource!.__type, propOverride.propertyId, out var result, entityGM2LDTK))
                                {
                                    Log.Write($"[{Log.ColorError}]Error processing value '{propOverride.value}' for field [{Log.ColorField}]{propOverride.varName} [[{propOverride.propertyId.varType}]][/].[/]");
                                    instance.CreateMetaFor<LDTKProject.Level.FieldInstance.MetaData>(fieldMeta.identifier, fieldMeta.Resource!.uid).GotError = true;
                                    continue;
                                }

                                instance.CreateOrExistingForced(fieldMeta.identifier, out LDTKProject.Level.FieldInstance fi, fieldMeta.uid);

                                fi.Meta.GotError = false;
                                fi.SetValues(result);
                            }

                            instance.RemoveUnusedMeta<LDTKProject.Level.FieldInstance.MetaData>(usedProperties, _meta =>
                            {
                                instance.Remove<LDTKProject.Level.FieldInstance>(_meta.identifier);
                            });
                        }

                        break;

                    case GMRTileLayer tileLayer:

                        LDTKProject.Tileset? tileset = tileLayer.tilesetId != null ? tilesetDict.GetValueOrDefault(tileLayer.tilesetId) : null;

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

                        layer.gridTiles = new List<LDTKProject.Level.Layer.TileInstance>(tilemap.Width * tilemap.Height);

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
                            var tile = new LDTKProject.Level.Layer.TileInstance();
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
}