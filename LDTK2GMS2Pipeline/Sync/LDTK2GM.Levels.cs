using System.Diagnostics;
using Dynamitey;
using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Utilities;
using Spectre.Console;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal partial class LDTK2GM
{
    /// <summary>
    /// Exports LDTK level back to GM
    /// </summary>
    /// <param name="_gmProject">GM project</param>
    /// <param name="_ldtkProject">LDTK project</param>
    /// <param name="_level">LDTK level</param>
    /// <param name="_entityLDTK2GM">Dictionary to convert entity instance's iids to GM's instance names</param>
    private static void ExportLevel(GMProject _gmProject, LDTKProject _ldtkProject, LDTKProject.Level _level, Dictionary<string, string> _entityLDTK2GM)
    {
        HashSet<string> knownObjects = _ldtkProject.GetResourceList<LDTKProject.Entity>().Where(t => t.Meta != null).Select(t => t.identifier).ToHashSet();

        GMRoom? room = _gmProject.FindResourceByName(_level.Meta?.identifier ?? _level.identifier, typeof(GMRoom)) as GMRoom;
        if (room == null)
        {
            Log.Push($"[{Log.ColorCreated}]Room [{Log.ColorLevel}]{_level.identifier}[/] created[/]", false);
            
            room = new GMRoom
            {
                name = _level.identifier
            };
            room.parent = _gmProject.FirstRoom?.parent;
            _gmProject.AddResource(room);
            Dynamic.InvokeMemberAction(_gmProject, nameof(GMProject.AddResourceToStorage), room );
            // Optional arguments are different in different DLL versions
            //_gmProject.AddResourceToStorage(room);
            _gmProject.RoomOrder.Add(room);
        }
        else
        {
            Log.Push($"Room [{Log.ColorLevel}]{_level.identifier}[/]");
        }

        using var levelStack = Log.PopOnDispose();

        if (_level.Meta == null)
        {
            _ldtkProject.CreateMetaFor(_level, room.name);
        }

        room.roomSettings.Width = _level.pxWid;
        room.roomSettings.Height = _level.pxHei;

        static string GetTrimmedName(string _name)
        {
            return _name.TrimStart('_');
        }

        // This allows to export layers with similar names to single GM layer. Supposed to be used for autotile layers
        foreach (var layerGroup in _ldtkProject.defs.layers.GroupBy(t => GetTrimmedName(t.identifier)))
        {
            List<(LDTKProject.Level.Layer Layer, bool initializingExisting)> filteredLayers = new(2);

            GMRLayer? gmLayer = room.layers.Find(t => GetTrimmedName(t.name) == layerGroup.Key);

            bool layerCreated = false;

            foreach (var layerDef in layerGroup)
            {
                var layerData = _level.GetResource<LDTKProject.Level.Layer>(layerDef.identifier);
                Debug.Assert(layerData != null, "layerData != null");

                bool hadLayer = false;

                if (gmLayer != null)
                {
                    if (!LDTKProject.Layer.CanBeConverted(gmLayer, layerDef.__type))
                    {
                        AnsiConsole.MarkupLineInterpolated($"[yellow]Layer types do not match for [green]{layerDef.identifier}[/] in [olive]{room.name}[/]: {gmLayer.GetType().Name} vs {layerDef.__type}. Delete it in GM if you want to receive data from it.[/]");
                        continue;
                    }

                    hadLayer = true;
                }
                else
                {
                    switch (layerDef.__type)
                    {
                        case LDTKProject.LayerTypes.Entities:
                            if (layerData.entityInstances.Count > 0)
                                gmLayer = new GMRInstanceLayer();
                            break;
                        case LDTKProject.LayerTypes.Tiles:
                            if (layerData.gridTiles.Count > 0)
                                gmLayer = new GMRTileLayer();
                            break;
                        //case LayerTypes.AutoLayer:
                        //case LayerTypes.IntGrid:
                        //    if ( layerData.autoLayerTiles.Count > 0 )
                        //        gmLayer = new GMRTileLayer();
                        //    break;
                        default:
                            continue;
                    }

                    if (gmLayer == null)
                        continue;

                    gmLayer.name = GetTrimmedName(layerDef.identifier);
                    gmLayer.gridX = layerDef.gridSize;
                    gmLayer.gridY = layerDef.gridSize;
                    gmLayer.parent = room;
                    gmLayer.Finalise();
                    room.layers.Add(gmLayer);
                    
                    if (!_gmProject.AddResource(gmLayer))
                        throw new Exception();

                    gmLayer.OwnerRoom = room;
                    gmLayer.parent = room;

                    layerCreated = true;
                }

                bool initializingExisting = false;
                if (layerData.Meta == null)
                {
                    initializingExisting = hadLayer;
                    _level.CreateMetaFor(layerData, layerDef.identifier);
                }

                filteredLayers.Add((layerData, initializingExisting));
            }
            
            if (filteredLayers.Count == 0)
                continue;
            
            if (layerCreated)
                Log.Push($"[{Log.ColorCreated}]Layer [{Log.ColorLayer}]{gmLayer.name}[/] created[/]", false);
            else
                Log.Push($"Layer [{Log.ColorLayer}]{gmLayer.name}[/]");

            using var layerStack = Log.PopOnDispose();

            UpdateLayerDepth(filteredLayers, gmLayer);

            switch (gmLayer)
            {
                case GMRInstanceLayer instanceLayer:
                    if (filteredLayers.Count > 1)
                    {
                        Log.Write($"[{Log.ColorWarning}]Multiple source entity layers found: [{Log.ColorLayer}]{
                            string.Join(',', filteredLayers.Select( t => t.Layer.__identifier))
                        }[/]. Using the first one only.[/]");
                    }
                    ExportEntities(instanceLayer, filteredLayers[0].Layer, filteredLayers[0].initializingExisting);
                    break;
                case GMRTileLayer tileLayer:
                    ExportTiles(tileLayer, filteredLayers);
                    break;
            }
        }

        ValidateRoomInstances(room);
        SortLayers(room);

        void UpdateLayerDepth(List<(LDTKProject.Level.Layer Layer, bool initializingExisting)> _ldtkLayers, GMRLayer _gmLayer)
        {
            LDTKProject.Level.FieldInstance? depthField = null;
            foreach (var ldtkLayer in _ldtkLayers)
            {
                depthField = _level.GetLayerDepthField(ldtkLayer.Layer);
                if (depthField != null)
                    break;
            }

            if (depthField == null)
                return;

            try
            {
                var value = Convert.ToInt32(depthField.__value.ToString());
                _gmLayer.userdefinedDepth = true;
                _gmLayer.depth = value;
            }
            catch (Exception)
            {
            }
        }

        void ExportEntities(GMRInstanceLayer _instanceLayer, LDTKProject.Level.Layer _layer, bool _initializingExisting)
        {
            var existing = _instanceLayer.instances.ToDictionary(t => t.name);

            if (!_initializingExisting)
            {
                foreach (var pair in _layer.EnumeratePairedResources<LDTKProject.Level.EntityInstance.MetaData, GMRInstance>(_instanceLayer.instances))
                {
                    if (pair.res == null)
                        continue;

                    bool isIgnored = pair.res.ignore || pair.res.objectId == null || !knownObjects.Contains(pair.res.objectId.name);
                    if (isIgnored)
                        continue;

                    bool shouldRemove = pair.meta == null || pair.meta.Resource == null;
                    if (!shouldRemove) 
                        continue;
                    
                    _instanceLayer.instances.Remove(pair.res);
                    _layer.Remove<LDTKProject.Level.EntityInstance>(pair.res.name);
                    
                    Log.Write($"[{Log.ColorDeleted}]Instance [{Log.ColorEntity}]{pair.res.objectId.name}[/] [[{pair.res.name}]] removed.[/]");
                }
            }

            foreach (LDTKProject.Level.EntityInstance instance in _layer.entityInstances)
            {
                if (!FindEntity(_gmProject, _ldtkProject, instance.defUid, out var entityType, out var obj))
                    continue;

                GMRInstance? gmInstance = null;
                if (instance.Meta != null)
                    existing.Remove(instance.Meta.identifier, out gmInstance);

                if (gmInstance == null)
                {
                    if (instance.Meta == null)
                        _layer.CreateMetaFor(instance, _entityLDTK2GM[instance.iid]);

                    gmInstance = new GMRInstance
                    {
                        name = instance.Meta!.identifier,
                        objectId = obj,
                        Owner = _instanceLayer,
                        parent = _instanceLayer,
                    };

                    gmInstance.AddToProject(_gmProject);
                    _instanceLayer.instances.Add(gmInstance);

                    Log.Push($"[{Log.ColorCreated}]Instance [{Log.ColorEntity}]{obj.name}[/] [[{gmInstance.name}]] created[/]", false);
                }
                else
                    Log.Push($"Instance [{Log.ColorEntity}]{obj.name}[/] [[{gmInstance.name}]]");

                using var instanceStack = Log.PopOnDispose();

                int x = instance.px[0];
                int y = instance.px[1];

                float scaleX = instance.width / (float)entityType.width;
                float scaleY = instance.height / (float)entityType.height;

                bool gotFlipProperty = false;
                bool flipX = false, flipY = false;
                int? imageIndex = null;

                var sourceProperties = gmInstance.properties.Where( t => t.varName != null).ToDictionary(t => t.varName);

                foreach (var data in instance.EnumeratePairedResources<LDTKProject.Level.FieldInstance.MetaData, GMOverriddenProperty>(gmInstance.properties, _property => _property.varName))
                {
                    if (data.res == null)
                        continue;

                    bool shouldRemove;
                    var fieldMeta = entityType.GetMeta<LDTKProject.Field.MetaData>(data.res.varName);
                    if (fieldMeta == null)
                        shouldRemove = false;
                    else
                        shouldRemove = (fieldMeta.Resource != null && (data.meta == null || (data.meta.Resource == null && !data.meta.GotError) || (data.meta.Resource != null && !data.meta.Resource.IsOverridden)));

                    if (shouldRemove)
                    {
                        Log.Write($"[{Log.ColorDeleted}]PropertyOverride [{Log.ColorField}]{data.res.varName}[/] removed.[/]");
                        gmInstance.properties.Remove(data.res);
                    }
                }

                foreach (LDTKProject.Level.FieldInstance fieldInstance in instance.fieldInstances)
                {
                    if (!fieldInstance.IsOverridden)
                        continue;

                    var fieldDef = entityType.GetResource<LDTKProject.Field>(fieldInstance.defUid);
                    if (fieldDef == null)
                    {
                        Log.Write($"[{Log.ColorError}]Field [{Log.ColorField}]{fieldInstance.__identifier}[/] not found.[/]");
                        continue;
                    }

                    string fieldName = fieldDef.Meta?.identifier ?? fieldDef.identifier;

                    if (fieldName == SharedData.FlipStateEnumName)
                    {
                        gotFlipProperty = true;
                        int index = SharedData.FlipProperty.listItems.IndexOf(fieldInstance.__value?.ToString());
                        if (index < 0)
                            continue;
                        flipX = (index & 1) > 0;
                        flipY = (index & 2) > 0;
                        continue;
                    }

                    if (fieldName == SharedData.ImageIndexState)
                    {
                        var parsedIndex = FieldConversion.LDTK2GM(_ldtkProject, fieldInstance.__value, fieldDef, SharedData.ImageIndexProperty, _entityLDTK2GM);

                        try
                        {
                            imageIndex = Convert.ToInt32(parsedIndex);
                        }
                        catch (Exception)
                        {
                            imageIndex = null;
                        }

                        continue;
                    }
                    
                    if (fieldInstance.Meta == null)
                        instance.CreateMetaFor(fieldInstance, fieldName);
                    else
                        fieldInstance.Meta.GotError = false;

                    bool isNew = false;
                    if (!sourceProperties.TryGetValue(fieldName, out var value))
                    {
                        var field = GMProjectUtilities.EnumerateAllProperties(gmInstance.objectId).FirstOrDefault(t => t.Property.varName == fieldName);
                        if (field == null)
                        {
                            Log.Write($"[{Log.ColorError}]Property [{Log.ColorField}]{fieldName}[/] not found.[/]");
                            continue;
                        }

                        if (fieldDef.Meta == null)
                        {
                            entityType.CreateMetaFor(fieldDef, field.Property.varName);
                        }

                        value = new GMOverriddenProperty(field.Property, field.DefinedIn, gmInstance);
                        gmInstance.properties.Add(value);

                        isNew = true;
                    }
  
                    var newValue = FieldConversion.LDTK2GM(_ldtkProject, fieldInstance.__value, fieldDef, value.propertyId, _entityLDTK2GM);

                    if (isNew || value.value != newValue)
                    {
                        if (!isNew)
                            Log.Write($"PropertyOverride [{Log.ColorField}]{fieldName}[/] had value changed from '{value.value}' to '{newValue}'");
                        else
                            Log.Write($"[{Log.ColorCreated}]PropertyOverride [{Log.ColorField}]{value.varName}[/] created. Value: '{newValue}'[/]");
                        
                        value.value = newValue;
                    }
                }

                if (!gotFlipProperty)
                {
                    flipX = gmInstance.flipX;
                    flipY = gmInstance.flipY;
                }

                if (flipX)
                    x -= (int)((entityType.pivotX - 0.5f) * 2f * instance.width);
                if (flipY)
                    y -= (int)((entityType.pivotY - 0.5f) * 2f * instance.height);

                gmInstance.x = x;
                gmInstance.y = y;
                gmInstance.scaleX = flipX ? -scaleX : scaleX;
                gmInstance.scaleY = flipY ? -scaleY : scaleY;

                if (imageIndex != null)
                    gmInstance.imageIndex = imageIndex.Value;
            }
        }

        void ExportTiles(GMRTileLayer _tileLayer, IList<(LDTKProject.Level.Layer layer, bool _initializingExisting)> _layersWithExtraData)
        {
            IList<LDTKProject.Level.Layer> layers = _layersWithExtraData.Where(t => !t._initializingExisting).Select(t => t.layer).ToList();
            if (layers.Count == 0)
                return;

            var usedTileset = layers.Select(t => t.__tilesetDefUid).FirstOrDefault(t => t != null);
            if (usedTileset == null)
            {
                _tileLayer.tilesetId = null;
                return;
            }

            if (layers.Count > 1 && !layers.All(t => t.__tilesetDefUid == null || t.__tilesetDefUid == usedTileset))
            {
                Log.Write($"[{Log.ColorError}]LDTK layers in group use different tilesets![/]");
                return;
            }

            LDTKProject.Tileset? tileset = _ldtkProject.GetResource<LDTKProject.Tileset>(usedTileset);

            var gmTileset = tileset?.Meta != null
                ? _gmProject.FindResourceByName(tileset.Meta.identifier, typeof(GMTileSet)) as GMTileSet
                : null;

            _tileLayer.tilesetId = gmTileset;
            if (gmTileset == null || tileset == null)
            {
                Log.Write($"[{Log.ColorError}]Tileset [{Log.ColorAsset}]{(tileset != null ? tileset.identifier : usedTileset)}[/] not found![/]");
                return;
            }

            var mainLayer = layers.First();

            if (layers.Count > 1 && !layers.All(t =>
                    t.pxOffsetX == mainLayer.pxOffsetX && t.pxOffsetY == mainLayer.pxOffsetY &&
                    t.__cWid == mainLayer.__cWid && t.__cHei == mainLayer.__cHei))
            {
                Log.Write($"[{Log.ColorError}]Sizes for layers in group are different![/]");
            }

            _tileLayer.x = mainLayer.pxOffsetX;
            _tileLayer.y = mainLayer.pxOffsetY;

            uint[,] newData = new uint[mainLayer.__cWid, mainLayer.__cHei];

            var gmTilesetWidth = (gmTileset.spriteId.width - gmTileset.tilexoff + gmTileset.tilehsep) / (gmTileset.tileWidth + gmTileset.tilehsep);

            foreach (LDTKProject.Level.Layer layer in layers.Reverse())
            {
                if (layer.__tilesetDefUid == null)
                    continue;

                foreach (LDTKProject.Level.Layer.TileInstance tile in layer.EnumerateAllTiles())
                {
                    bool flipX = (tile.f & 2) > 0;
                    bool flipY = (tile.f & 1) > 0;

                    int tileX = tile.src[0] / layer.__gridSize;
                    int tileY = tile.src[1] / layer.__gridSize;

                    int x = tile.px[0] / layer.__gridSize;
                    int y = tile.px[1] / layer.__gridSize;

                    uint finalIndex = (uint)(tileX + tileY * gmTilesetWidth);
                    if (flipX)
                        finalIndex |= TileMap.TileBitMask_Flip;
                    if (flipY)
                        finalIndex |= TileMap.TileBitMask_Mirror;

                    if (x < 0 || y < 0 || x >= layer.__cWid || y >= layer.__cHei)
                    {
                        AnsiConsole.WriteException(new IndexOutOfRangeException($"Out of range: {x}, {y} not in range {layer.__cWid}, {layer.__cHei}, level {_level.identifier}"));
                        continue;
                    }

                    newData[x, y] = finalIndex;
                }
            }

            if (!TilemapsEqual(_tileLayer.tiles.Tiles, newData))
            {
                Log.Write($"Tiles changed.");
                _tileLayer.tiles.Tiles = newData;
            }
        }

        void ValidateRoomInstances(GMRoom _room)
        {
            var existingInstances = _room.layers.Where(t => t is GMRInstanceLayer).SelectMany(t => ((GMRInstanceLayer)t).instances).ToHashSet();

            ResourceList<GMRInstance> list = _room.instanceCreationOrder;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var instance = list[i];
                if (!existingInstances.Contains(instance))
                {
                    existingInstances.Remove(instance);
                    list.RemoveAt(i);
                }
            }

            existingInstances.ExceptWith(_room.instanceCreationOrder);

            foreach (GMRInstance instance in existingInstances)
            {
                list.Add(instance);
            }
        }

        void SortLayers(GMRoom _room)
        {
            List<string> orderInfo = new(_room.layers.Count);
            foreach (var layer in _level.layerInstances)
            {
                orderInfo.Add(layer.__identifier);
            }

            for (int i = 0; i < _room.layers.Count; i++)
            {
                var layer = _room.layers[i];
                if (orderInfo.IndexOf(layer.name) > 0)
                    continue;

                int insertAt = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    int layerIndex = orderInfo.IndexOf(_room.layers[j].name);
                    if (layerIndex < 0)
                        continue;

                    insertAt = layerIndex + 1;
                    break;
                }

                orderInfo.Insert(insertAt, layer.name);
            }

            _room.layers.Sort((l, r) =>
            {
                var indexLeft = orderInfo.IndexOf(l.name);
                var indexRight = orderInfo.IndexOf(r.name);
                return indexLeft.CompareTo(indexRight);
            });
        }
    }

    private const uint TileMaxIgnoredMask = ~(TileMap.TileBitMask_ColourIndex | TileMap.TileBitMask_Inherit | TileMap.TileBitMask_Rotate90);

    private static bool TilemapsEqual(uint[,] _left, uint[,] _right)
    {
        if (_left.GetLength(0) != _right.GetLength(0))
            return false;

        if (_left.GetLength(1) != _right.GetLength(1))
            return false;

        for (int y = _left.GetLength(1) - 1; y >= 0; y--)
        for (int x = _left.GetLength(0) - 1; x >= 0; x--)
        {
            var l = _left[x, y] & TileMaxIgnoredMask;
            var r = _right[x, y] & TileMaxIgnoredMask;
            if (l != r)
                return false;
        }

        return true;
    }
}