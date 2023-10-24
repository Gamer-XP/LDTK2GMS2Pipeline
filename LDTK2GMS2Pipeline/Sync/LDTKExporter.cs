using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using LDTK2GMS2Pipeline.LDTK;
using Spectre.Console;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject.Enum;

namespace LDTK2GMS2Pipeline.Sync;

internal class LDTKExporter
{
    public static async Task ExportToGM( GMProject _gmProject, LDTKProject _ldtkProject )
    {
        ProjectInfo.IsLoading = false;

        foreach (LDTKProject.Level level in _ldtkProject.levels)
        {
            ExportLevel( _gmProject, _ldtkProject, level);
        }

        await _ldtkProject.SaveMeta();
    }

    private static void ExportLevel( GMProject _gmProject, LDTKProject _ldtkProject, LDTKProject.Level _level )
    {
        HashSet<string> knownObjects = _ldtkProject.GetResourceList<Entity>().Where( t => t.Meta != null).Select(t => t.identifier).ToHashSet();

        GMRoom? room = _gmProject.FindResourceByName(_level.Meta?.identifier ?? _level.identifier, typeof(GMRoom)) as GMRoom;
        if (room == null)
        {
            AnsiConsole.MarkupLineInterpolated($"New room [teal]{_level.identifier}[/] added to GM project.");
            room = new GMRoom
            {
                name = _level.identifier
            };
            room.parent = _gmProject.FirstRoom?.parent;
            _gmProject.AddResource(room);
            _gmProject.AddResourceToStorage(room);
            _gmProject.RoomOrder.Add(room);
        }

        if ( _level.Meta == null )
        {
            _ldtkProject.CreateMetaFor(_level, room.name );
        }

        room.roomSettings.Width = _level.pxWid;
        room.roomSettings.Height = _level.pxHei;

        foreach (LDTKProject.Level.Layer layer in _level.layerInstances)
        {
            LDTKProject.Layer? layerDef = _ldtkProject.GetResource<LDTKProject.Layer>( layer.layerDefUid );
            if (layerDef == null)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Unable to find layer {layer.__identifier}. Not supported to happen[/]");
                continue;
            }

            GMRLayer? gmLayer = room.layers.Find( t => t.name == layer.__identifier );
            if (gmLayer != null)
            {
                if ( !LDTKProject.Layer.CanBeConverted( gmLayer, layerDef.__type ) )
                {
                    AnsiConsole.MarkupLineInterpolated( $"[yellow]Layer types do not match for [green]{layerDef.identifier}[/] in [olive]{room.name}[/]: {gmLayer.GetType().Name} vs {layerDef.__type}. Delete it in GM if you want to receive data from it.[/]" );
                    continue;
                }
            }
            else
            {
                switch (layerDef.__type)
                {
                    case LDTKProject.LayerTypes.Entities:
                        if (layer.entityInstances.Count > 0)
                            gmLayer = new GMRInstanceLayer();
                        break;
                    case LDTKProject.LayerTypes.Tiles:
                        if ( layer.gridTiles.Count > 0)
                            gmLayer = new GMRTileLayer();
                        break;
                    default:
                        continue;
                }

                if ( gmLayer == null)
                    continue;

                gmLayer.name = layerDef.identifier;
                room.layers.Add(gmLayer);
                gmLayer.Finalise();
                if (!_gmProject.AddResource(gmLayer))
                    throw new Exception();
                AnsiConsole.MarkupLineInterpolated($"Added layer [green]{gmLayer.name}[/] to room [teal]{room.name}[/]");
            }

            if (layer.Meta == null) 
                _level.CreateMetaFor(layer, layerDef.identifier);

            switch ( gmLayer )
            {
                case GMRInstanceLayer instanceLayer:
                    ExportEntities(instanceLayer, layer , layerDef);
                    break;
                case GMRTileLayer tileLayer:
                    ExportTiles(tileLayer, layer, layerDef );
                    break;
            }
        }

        void ExportEntities( GMRInstanceLayer _instanceLayer, LDTKProject.Level.Layer _layer, LDTKProject.Layer _layerDef )
        {
            bool FindObject( int _entityUid, out LDTKProject.Entity _entity, out GMObject _gmObject )
            {
                _entity = default!;
                _gmObject = default!;

                var entity = _ldtkProject.GetResource<LDTKProject.Entity>(_entityUid);
                if (entity == null)
                    return false;

                var gmObjectName = entity.Meta?.identifier;
                if (gmObjectName == null)
                    return false;

                var obj = _gmProject.FindResourceByName(gmObjectName, typeof(GMObject)) as GMObject;
                if (obj == null)
                    return false;

                _gmObject = obj;
                _entity = entity;
                return true;
            }

            var existing = _instanceLayer.instances.ToDictionary( t => t.name);

            _layer.RemoveDeletedItems<Level.EntityInstance.MetaData, GMRInstance>( _instanceLayer.instances, ( _instance , _) => !_instance.ignore && knownObjects.Contains(_instance.objectId.name) );

            foreach (LDTKProject.Level.EntityInstance instance in _layer.entityInstances)
            {
                if (!FindObject( instance.defUid, out var entityType, out var obj ) )
                    continue;

                GMRInstance? gmInstance = null;
                if (instance.Meta != null)
                    existing.Remove(instance.Meta.identifier, out gmInstance);

                if (gmInstance == null)
                {
                    if ( instance.Meta == null )
                        _layer.CreateMetaFor( instance, GetRandomInstanceName( _gmProject ) );

                    gmInstance = new GMRInstance
                    {
                        name = instance.Meta.identifier,
                        objectId = obj
                    };
                    _gmProject.AddResource(gmInstance);
                    AnsiConsole.MarkupLineInterpolated($"Added instance [underline]{gmInstance.name}[/] [teal][[{obj.name}]][/] on layer {_instanceLayer.name}");

                    _instanceLayer.instances.Add( gmInstance );
                }

                int x = instance.px[0];
                int y = instance.px[1];

                float scaleX = instance.width / (float) entityType.width;
                float scaleY = instance.height / (float) entityType.height;

                bool flipX = false, flipY = false;

                var sourceProperties = gmInstance.properties.ToDictionary(t => t.varName);

                int count = instance.RemoveDeletedItems<Level.FieldInstance.MetaData, GMOverriddenProperty>(gmInstance.properties, 
                    (_, m) => m == null || !m.GotError, 
                    t => t.varName, 
                    t => t.Resource == null || t.Resource.IsOverridden || t.GotError );
                if ( count > 0)
                    Console.WriteLine($"Removed {count} from {gmInstance.objectId.name}");
       
                foreach ( LDTKProject.Level.FieldInstance fieldInstance in instance.fieldInstances )
                {
                    if ( !fieldInstance.IsOverridden )
                        continue;

                    var fieldDef = entityType.GetResource<LDTKProject.Field>( fieldInstance.defUid );
                    if ( fieldDef == null || fieldDef.Meta == null )
                    {
                        AnsiConsole.MarkupLineInterpolated( $"[red]Unable to find field definition for [underline]{fieldInstance.__identifier}[/][/]" );
                        continue;
                    }

                    if (fieldDef.Meta.identifier == SharedData.FlipStateEnumName)
                    {
                        int index = SharedData.FlipProperty.listItems.IndexOf(fieldInstance.__value?.ToString());
                        if ( index < 0)
                            continue;
                        flipX = (index & 1) > 0;
                        flipY = (index & 2) > 0;
                        continue;
                    }

                    if ( !sourceProperties.TryGetValue( fieldDef.Meta.identifier, out var value))
                    {
                        var field = GMProjectUtilities.EnumerateAllProperties( gmInstance.objectId ).FirstOrDefault(t => t.Property.varName == fieldDef.Meta.identifier );
                        if (field == null)
                        {
                            AnsiConsole.MarkupLineInterpolated($"[red]There is no field with name [green]{fieldDef.Meta.identifier}[/] in [teal]{gmInstance.objectId.name}[/][/]");
                            continue;
                        }

                        value = new GMOverriddenProperty( field.Property, field.DefinedIn, gmInstance );
                        gmInstance.properties.Add(value);
                    }

                    if (fieldInstance.Meta == null)
                        instance.CreateMetaFor(fieldInstance, fieldDef.Meta.identifier);
                    else
                        fieldInstance.Meta.GotError = false;

                    value.value = fieldDef.Meta.LDTK2GM( fieldInstance.__value );
                }

                if ( flipX )
                    x -= (int) ((entityType.pivotX - 0.5f) * 2f * instance.width);
                if ( flipY )
                    y -= (int) ((entityType.pivotY - 0.5f) * 2f * instance.height);

                gmInstance.x = x;
                gmInstance.y = y;
                gmInstance.scaleX = flipX? -scaleX : scaleX;
                gmInstance.scaleY = flipY? -scaleY : scaleY;
            }
        }

        void ExportTiles(GMRTileLayer _tileLayer, LDTKProject.Level.Layer _layer, LDTKProject.Layer _layerDef )
        {
            Tileset? tileset = _layer.overrideTilesetUid != null
                ? _ldtkProject.GetResource<Tileset>(_layer.overrideTilesetUid )
                : null;

            var gmTileset = tileset?.Meta != null
                ? _gmProject.FindResourceByName(tileset.Meta.identifier, typeof(GMTileSet)) as GMTileSet
                : null;

            _tileLayer.tilesetId = gmTileset;
            if (gmTileset == null)
            {
                if ( _layer.overrideTilesetUid != null)
                    AnsiConsole.MarkupLineInterpolated($"[red]Unable to find matching tileset with id{_layer.overrideTilesetUid}[/]");
                return;
            }

            _tileLayer.x = _layer.pxOffsetX;
            _tileLayer.y = _layer.pxOffsetY;

            uint[,] newData = new uint[_layer.__cWid, _layer.__cHei];

            var gmTilesetWidth = (gmTileset.spriteId.width - gmTileset.tilexoff + gmTileset.tilehsep) / (gmTileset.tileWidth + gmTileset.tilehsep);

            foreach (Level.Layer.TileInstance tile in _layer.gridTiles)
            {
                bool flipX = (tile.f & 2) > 0;
                bool flipY = (tile.f & 1) > 0;
                int tilemapIndex = tile.d[0];

                int tileIndex = tile.t;
                int tileX = tileIndex % tileset.__cWid;
                int tileY = tileIndex / tileset.__cWid;

                int x = tilemapIndex % _layer.__cWid;
                int y = tilemapIndex / _layer.__cWid;

                uint finalIndex = (uint)(tileX + tileY * gmTilesetWidth);
                if (flipX)
                    finalIndex |= TileMap.TileBitMask_Flip;
                if ( flipY )
                    finalIndex |= TileMap.TileBitMask_Mirror;

                if (x < 0 || y < 0 || x >= _layer.__cWid || y >= _layer.__cHei)
                {
                    throw new IndexOutOfRangeException($"Out of range: {x}, {y} not in range {_layer.__cWid}, {_layer.__cHei}, level {_level.identifier}");
                }

                newData[x, y] = finalIndex;
            }

            if (!TilemapsEqual(_tileLayer.tiles.Tiles, newData))
            {
                AnsiConsole.MarkupLineInterpolated($"Tiles changed at layer [green]{_tileLayer.name}[/] in [teal]{room.name}[/]");
                _tileLayer.tiles.Tiles = newData;
            }
        }
    }

    private const uint TileMaxIgnoredMask = ~(TileMap.TileBitMask_ColourIndex | TileMap.TileBitMask_Inherit | TileMap.TileBitMask_Rotate90);

    private static bool TilemapsEqual( uint[,] _left, uint[,] _right )
    {
        if (_left.GetLength(0) != _right.GetLength(0))
            return false;

        if ( _left.GetLength(1) != _right.GetLength(1))
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

    private static Random UniqueNameRandom = new Random();

    private static string GetRandomInstanceName( GMProject _project )
    {
        while (true)
        {
            string result = "inst_" + UniqueNameRandom.Next().ToString( "X" );
            if (_project.FindResourceByName(result, typeof(GMRInstance)) == null)
                return result;
        }
    }
}
