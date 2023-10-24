using System.Diagnostics;
using LDTK2GMS2Pipeline.LDTK;
using Spectre.Console;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;

namespace LDTK2GMS2Pipeline.Sync;

internal class LDTK2GM
{
    public static async Task ExportToGM( GMProject _gmProject, LDTKProject _ldtkProject )
    {
        ProjectInfo.IsLoading = false;

        foreach ( LDTKProject.Level level in _ldtkProject.levels )
        {
            ExportLevel( _gmProject, _ldtkProject, level );
        }

        await _ldtkProject.SaveMeta();
    }

    private static void ExportLevel( GMProject _gmProject, LDTKProject _ldtkProject, LDTKProject.Level _level )
    {
        HashSet<string> knownObjects = _ldtkProject.GetResourceList<Entity>().Where( t => t.Meta != null ).Select( t => t.identifier ).ToHashSet();

        GMRoom? room = _gmProject.FindResourceByName( _level.Meta?.identifier ?? _level.identifier, typeof( GMRoom ) ) as GMRoom;
        if ( room == null )
        {
            AnsiConsole.MarkupLineInterpolated( $"New room [teal]{_level.identifier}[/] added to GM project." );
            room = new GMRoom
            {
                name = _level.identifier
            };
            room.parent = _gmProject.FirstRoom?.parent;
            _gmProject.AddResource( room );
            _gmProject.AddResourceToStorage( room );
            _gmProject.RoomOrder.Add( room );
        }

        if ( _level.Meta == null )
        {
            _ldtkProject.CreateMetaFor( _level, room.name );
        }

        room.roomSettings.Width = _level.pxWid;
        room.roomSettings.Height = _level.pxHei;

        static string GetTrimmedName( string _name )
        {
            return _name.TrimStart('_');
        }

        foreach (var layerGroup in _ldtkProject.defs.layers.GroupBy( t => GetTrimmedName(t.identifier) ).Reverse() )
        {
            List<Level.Layer> filteredLayers = new( 2 );

            GMRLayer? gmLayer = room.layers.Find(t => GetTrimmedName(t.name) == layerGroup.Key);

            foreach (Layer layerDef in layerGroup)
            {
                var layerData = _level.GetResource<Level.Layer>( layerDef.identifier );
                Debug.Assert( layerData != null, "layerData != null");

                if ( gmLayer != null )
                {
                    if ( !LDTKProject.Layer.CanBeConverted( gmLayer, layerDef.__type ) )
                    {
                        AnsiConsole.MarkupLineInterpolated( $"[yellow]Layer types do not match for [green]{layerDef.identifier}[/] in [olive]{room.name}[/]: {gmLayer.GetType().Name} vs {layerDef.__type}. Delete it in GM if you want to receive data from it.[/]" );
                        continue;
                    }
                }
                else
                {
                    switch ( layerDef.__type )
                    {
                        case LayerTypes.Entities:
                            if ( layerData.entityInstances.Count > 0 )
                                gmLayer = new GMRInstanceLayer();
                            break;
                        case LayerTypes.Tiles:
                            if ( layerData.gridTiles.Count > 0 )
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

                    if ( gmLayer == null )
                        continue;

                    gmLayer.name = GetTrimmedName(layerDef.identifier);
                    room.layers.Add( gmLayer );
                    gmLayer.Finalise();
                    if ( !_gmProject.AddResource( gmLayer ) )
                        throw new Exception();
                    AnsiConsole.MarkupLineInterpolated( $"Added layer [green]{gmLayer.name}[/] to room [teal]{room.name}[/]" );
                }

                if ( layerData.Meta == null )
                    _level.CreateMetaFor( layerData, layerDef.identifier );

                filteredLayers.Add( layerData );
            }

            if ( filteredLayers.Count == 0)
                continue;


            switch ( gmLayer )
            {
                case GMRInstanceLayer instanceLayer:
                    ExportEntities( instanceLayer, filteredLayers.First() );
                    break;
                case GMRTileLayer tileLayer:
                    ExportTiles( tileLayer, filteredLayers );
                    break;
            }
        }

        void ExportEntities( GMRInstanceLayer _instanceLayer, LDTKProject.Level.Layer _layer )
        {
            bool FindObject( int _entityUid, out LDTKProject.Entity _entity, out GMObject _gmObject )
            {
                _entity = default!;
                _gmObject = default!;

                var entity = _ldtkProject.GetResource<LDTKProject.Entity>( _entityUid );
                if ( entity == null )
                    return false;

                var gmObjectName = entity.Meta?.identifier;
                if ( gmObjectName == null )
                    return false;

                var obj = _gmProject.FindResourceByName( gmObjectName, typeof( GMObject ) ) as GMObject;
                if ( obj == null )
                    return false;

                _gmObject = obj;
                _entity = entity;
                return true;
            }

            var existing = _instanceLayer.instances.ToDictionary( t => t.name );

            _layer.RemoveDeletedItems<Level.EntityInstance.MetaData, GMRInstance>( _instanceLayer.instances, ( _instance, _ ) => !_instance.ignore && knownObjects.Contains( _instance.objectId.name ) );

            foreach ( LDTKProject.Level.EntityInstance instance in _layer.entityInstances )
            {
                if ( !FindObject( instance.defUid, out var entityType, out var obj ) )
                    continue;

                GMRInstance? gmInstance = null;
                if ( instance.Meta != null )
                    existing.Remove( instance.Meta.identifier, out gmInstance );

                if ( gmInstance == null )
                {
                    if ( instance.Meta == null )
                        _layer.CreateMetaFor( instance, GetRandomInstanceName( _gmProject ) );

                    gmInstance = new GMRInstance
                    {
                        name = instance.Meta.identifier,
                        objectId = obj
                    };
                    _gmProject.AddResource( gmInstance );
                    AnsiConsole.MarkupLineInterpolated( $"Added instance [underline]{gmInstance.name}[/] [teal][[{obj.name}]][/] on layer {_instanceLayer.name}" );

                    _instanceLayer.instances.Add( gmInstance );
                }

                int x = instance.px[0];
                int y = instance.px[1];

                float scaleX = instance.width / (float) entityType.width;
                float scaleY = instance.height / (float) entityType.height;

                bool flipX = false, flipY = false;

                var sourceProperties = gmInstance.properties.ToDictionary( t => t.varName );

                int count = instance.RemoveDeletedItems<Level.FieldInstance.MetaData, GMOverriddenProperty>( gmInstance.properties,
                    ( _, m ) => m == null || !m.GotError,
                    t => t.varName,
                    t => t.Resource == null || t.Resource.IsOverridden || t.GotError );
                if ( count > 0 )
                    Console.WriteLine( $"Removed {count} from {gmInstance.objectId.name}" );

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

                    if ( fieldDef.Meta.identifier == SharedData.FlipStateEnumName )
                    {
                        int index = SharedData.FlipProperty.listItems.IndexOf( fieldInstance.__value?.ToString() );
                        if ( index < 0 )
                            continue;
                        flipX = (index & 1) > 0;
                        flipY = (index & 2) > 0;
                        continue;
                    }

                    if ( !sourceProperties.TryGetValue( fieldDef.Meta.identifier, out var value ) )
                    {
                        var field = GMProjectUtilities.EnumerateAllProperties( gmInstance.objectId ).FirstOrDefault( t => t.Property.varName == fieldDef.Meta.identifier );
                        if ( field == null )
                        {
                            AnsiConsole.MarkupLineInterpolated( $"[red]There is no field with name [green]{fieldDef.Meta.identifier}[/] in [teal]{gmInstance.objectId.name}[/][/]" );
                            continue;
                        }

                        value = new GMOverriddenProperty( field.Property, field.DefinedIn, gmInstance );
                        gmInstance.properties.Add( value );
                    }

                    if ( fieldInstance.Meta == null )
                        instance.CreateMetaFor( fieldInstance, fieldDef.Meta.identifier );
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
                gmInstance.scaleX = flipX ? -scaleX : scaleX;
                gmInstance.scaleY = flipY ? -scaleY : scaleY;
            }
        }

        void ExportTiles( GMRTileLayer _tileLayer, IList<LDTKProject.Level.Layer> _layers )
        {
            var usedTileset = _layers.Select( t => t.__tilesetDefUid ).FirstOrDefault( t => t != null );
            if ( usedTileset == null )
            {
                _tileLayer.tilesetId = null;
                return;
            }

            if ( _layers.Count > 1 && !_layers.All( t => t.__tilesetDefUid == null || t.__tilesetDefUid == usedTileset ) )
            {
                AnsiConsole.MarkupLineInterpolated( $"[red]Layers of group [underline]{_tileLayer.name}[/] in [teal]{_level.identifier}[/] use different tilesets. This is not supported.[/]" );
                return;
            }

            Tileset? tileset = _ldtkProject.GetResource<Tileset>(usedTileset);

            var gmTileset = tileset?.Meta != null
                ? _gmProject.FindResourceByName( tileset.Meta.identifier, typeof( GMTileSet ) ) as GMTileSet
                : null;

            _tileLayer.tilesetId = gmTileset;
            if ( gmTileset == null || tileset == null )
            {
                AnsiConsole.MarkupLineInterpolated( $"[red]Unable to find matching GM tileset for {(tileset != null? tileset.identifier : usedTileset)}[/]" );
                return;
            }

            var mainLayer = _layers.First();

            if (_layers.Count > 0 && !_layers.All(t =>
                    t.pxOffsetX == mainLayer.pxOffsetX && t.pxOffsetY == mainLayer.pxOffsetY &&
                    t.__cWid == mainLayer.__cWid && t.__cHei == mainLayer.__cHei))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Sizes for layers in group {_tileLayer.name} in level {_level.identifier} are different![/]");
            }

            _tileLayer.x = mainLayer.pxOffsetX;
            _tileLayer.y = mainLayer.pxOffsetY;

            uint[,] newData = new uint[mainLayer.__cWid, mainLayer.__cHei];

            var gmTilesetWidth = (gmTileset.spriteId.width - gmTileset.tilexoff + gmTileset.tilehsep) / (gmTileset.tileWidth + gmTileset.tilehsep);

            foreach (Level.Layer layer in _layers.Reverse())
            {
                if ( layer.__tilesetDefUid == null)
                    continue;

                foreach (Level.Layer.TileInstance tile in layer.EnumerateAllTiles() )
                {
                    bool flipX = (tile.f & 2) > 0;
                    bool flipY = (tile.f & 1) > 0;

                    int tileIndex = tile.t;
                    int tileX = tile.src[0] / layer.__gridSize;
                    int tileY = tile.src[1] / layer.__gridSize;

                    int x = tile.px[0] / layer.__gridSize;
                    int y = tile.px[1] / layer.__gridSize;

                    uint finalIndex = (uint) (tileX + tileY * gmTilesetWidth);
                    if ( flipX )
                        finalIndex |= TileMap.TileBitMask_Flip;
                    if ( flipY )
                        finalIndex |= TileMap.TileBitMask_Mirror;

                    if ( x < 0 || y < 0 || x >= layer.__cWid || y >= layer.__cHei )
                    {
                        throw new IndexOutOfRangeException( $"Out of range: {x}, {y} not in range {layer.__cWid}, {layer.__cHei}, level {_level.identifier}" );
                    }

                    newData[x, y] = finalIndex;
                }
            }

            if ( !TilemapsEqual( _tileLayer.tiles.Tiles, newData ) )
            {
                AnsiConsole.MarkupLineInterpolated( $"Tiles changed at layer [green]{_tileLayer.name}[/] in [teal]{room.name}[/]" );
                _tileLayer.tiles.Tiles = newData;
            }
        }
    }

    private const uint TileMaxIgnoredMask = ~(TileMap.TileBitMask_ColourIndex | TileMap.TileBitMask_Inherit | TileMap.TileBitMask_Rotate90);

    private static bool TilemapsEqual( uint[,] _left, uint[,] _right )
    {
        if ( _left.GetLength( 0 ) != _right.GetLength( 0 ) )
            return false;

        if ( _left.GetLength( 1 ) != _right.GetLength( 1 ) )
            return false;

        for ( int y = _left.GetLength( 1 ) - 1; y >= 0; y-- )
            for ( int x = _left.GetLength( 0 ) - 1; x >= 0; x-- )
            {
                var l = _left[x, y] & TileMaxIgnoredMask;
                var r = _right[x, y] & TileMaxIgnoredMask;
                if ( l != r )
                    return false;
            }

        return true;
    }

    private static Random UniqueNameRandom = new Random();

    private static string GetRandomInstanceName( GMProject _project )
    {
        while ( true )
        {
            string result = "inst_" + UniqueNameRandom.Next().ToString( "X" );
            if ( _project.FindResourceByName( result, typeof( GMRInstance ) ) == null )
                return result;
        }
    }
}
