using System.Diagnostics;
using System.Text.Json;
using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Utilities;
using Spectre.Console;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject.Level;

namespace LDTK2GMS2Pipeline.Sync;

internal class LDTK2GM
{
    public static async Task ExportToGM( GMProject _gmProject, LDTKProject _ldtkProject )
    {
        ProjectInfo.IsLoading = false;

        var matches = InitializeEntityMatches(_gmProject, _ldtkProject);

        foreach ( LDTKProject.Level level in _ldtkProject.levels )
        {
            ExportLevel( _gmProject, _ldtkProject, level, matches );
        }
    }

    /// <summary>
    /// Returns dictionary for LDTK -> GM entity instance identifier matches. Will generate a new id if Meta is missing
    /// </summary>
    private static Dictionary<string, string> InitializeEntityMatches( GMProject _gmProject, LDTKProject _ldtkProject  )
    {
        var objects = _gmProject.GetResourcesByType<GMObject>();

        Dictionary<string, string> result = new();

        foreach ( var layer in _ldtkProject.levels.SelectMany( t => t.layerInstances ) )
        {
            foreach (EntityInstance instance in layer.entityInstances)
            {
                string id = instance.Meta?.identifier ?? GetRandomInstanceName(_gmProject);

                result.Add(instance.iid, id );
            }
        }

        return result;
    }

    private static bool FindObject( GMProject _gmProject, LDTKProject _ldtkProject, int _entityUid, out LDTKProject.Entity _entity, out GMObject _gmObject )
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

    private static void ExportLevel( GMProject _gmProject, LDTKProject _ldtkProject, LDTKProject.Level _level, Dictionary<string, string> _entityLDTK2GM )
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

        foreach (var layerGroup in _ldtkProject.defs.layers.GroupBy( t => GetTrimmedName(t.identifier) ) )
        {
            List<(Level.Layer Layer, bool initializingExisting) > filteredLayers = new( 2 );

            GMRLayer? gmLayer = room.layers.Find(t => GetTrimmedName(t.name) == layerGroup.Key);

            foreach (var layerDef in layerGroup)
            {
                var layerData = _level.GetResource<Level.Layer>( layerDef.identifier );
                Debug.Assert( layerData != null, "layerData != null");

                bool hadLayer = false;

                if ( gmLayer != null )
                {
                    if ( !LDTKProject.Layer.CanBeConverted( gmLayer, layerDef.__type ) )
                    {
                        AnsiConsole.MarkupLineInterpolated( $"[yellow]Layer types do not match for [green]{layerDef.identifier}[/] in [olive]{room.name}[/]: {gmLayer.GetType().Name} vs {layerDef.__type}. Delete it in GM if you want to receive data from it.[/]" );
                        continue;
                    }

                    hadLayer = true;
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

                bool initializingExisting = false;
                if (layerData.Meta == null)
                {
                    initializingExisting = hadLayer;
                    _level.CreateMetaFor(layerData, layerDef.identifier);
                }

                filteredLayers.Add( (layerData, initializingExisting) );
            }

            if ( filteredLayers.Count == 0)
                continue;

            switch ( gmLayer )
            {
                case GMRInstanceLayer instanceLayer:
                    ExportEntities( instanceLayer, filteredLayers[0].Layer, filteredLayers[0].initializingExisting );
                    break;
                case GMRTileLayer tileLayer:
                    ExportTiles( tileLayer, filteredLayers );
                    break;
            }
        }

        ValidateRoomInstances(room);
        SortLayers(room);

        void ExportEntities( GMRInstanceLayer _instanceLayer, LDTKProject.Level.Layer _layer, bool _initializingExisting )
        {
            var existing = _instanceLayer.instances.ToDictionary( t => t.name );

            if (!_initializingExisting )
                foreach (var pair in _layer.EnumeratePairedResources<Level.EntityInstance.MetaData, GMRInstance>(_instanceLayer.instances))
                {
                    if (pair.res == null)
                        continue;

                    bool isIgnored = pair.res.ignore || pair.res.objectId == null || !knownObjects.Contains(pair.res.objectId.name);
                    if (isIgnored)
                        continue;

                    bool shouldRemove = pair.meta == null || pair.meta.Resource == null;
                    if (shouldRemove)
                    {
                        AnsiConsole.MarkupLineInterpolated($"Removed [teal]{pair.res.objectId.name}[/] from [olive]{room.name}[/]");
                        _instanceLayer.instances.Remove( pair.res );
                        _layer.Remove<Level.EntityInstance>( pair.res.name);
                    }
                }

            foreach ( LDTKProject.Level.EntityInstance instance in _layer.entityInstances )
            {
                if ( !FindObject( _gmProject, _ldtkProject, instance.defUid, out var entityType, out var obj ) )
                    continue;

                GMRInstance? gmInstance = null;
                if ( instance.Meta != null )
                    existing.Remove( instance.Meta.identifier, out gmInstance );

                if ( gmInstance == null )
                {
                    if (instance.Meta == null) 
                        _layer.CreateMetaFor(instance, _entityLDTK2GM[instance.iid]);

                    gmInstance = new GMRInstance
                    {
                        name = instance.Meta!.identifier,
                        objectId = obj,
                        parent = _instanceLayer,
                    };

                    gmInstance.parent = _instanceLayer;
                    gmInstance.Owner = _instanceLayer;

                    gmInstance.AddToProject(_gmProject);
                    _instanceLayer.instances.Add( gmInstance );

                    AnsiConsole.MarkupLineInterpolated( $"Added [teal]{obj.name}[/] to [olive]{room.name}[/]" );
                }

                int x = instance.px[0];
                int y = instance.px[1];

                float scaleX = instance.width / (float) entityType.width;
                float scaleY = instance.height / (float) entityType.height;

                bool gotFlipProperty = false;
                bool flipX = false, flipY = false;
                int? imageIndex = null;

                var sourceProperties = gmInstance.properties.ToDictionary( t => t.varName );

                foreach (var data in instance.EnumeratePairedResources<Level.FieldInstance.MetaData, GMOverriddenProperty>( gmInstance.properties, _property => _property.varName ))
                {
                    if ( data.res == null)
                        continue;

                    bool shouldRemove;
                    var fieldMeta = entityType.GetMeta<Field.MetaData>(data.res.varName);
                    if (fieldMeta == null)
                        shouldRemove = false;
                    else
                        shouldRemove = fieldMeta.Resource != null && ( data.meta == null || (data.meta.Resource == null && !data.meta.GotError));

                    if (shouldRemove)
                    {
                        AnsiConsole.MarkupLineInterpolated( $"Removed [underline]{data.res.varName}[/] override from [olive]{gmInstance.name}[/] [[{gmInstance.objectId.name}]] in [teal]{room.name}[/]" );
                        gmInstance.properties.Remove(data.res);
                    }
                }

                foreach ( LDTKProject.Level.FieldInstance fieldInstance in instance.fieldInstances )
                {
                    if ( !fieldInstance.IsOverridden )
                        continue;

                    var fieldDef = entityType.GetResource<LDTKProject.Field>( fieldInstance.defUid );
                    if ( fieldDef == null )
                    {
                        AnsiConsole.MarkupLineInterpolated( $"[red]Unable to find field definition for [underline]{fieldInstance.__identifier}[/][/]" );
                        continue;
                    }

                    string fieldName = fieldDef.Meta?.identifier ?? fieldDef.identifier;

                    if ( fieldName == SharedData.FlipStateEnumName )
                    {
                        gotFlipProperty = true;
                        int index = SharedData.FlipProperty.listItems.IndexOf( fieldInstance.__value?.ToString() );
                        if ( index < 0 )
                            continue;
                        flipX = (index & 1) > 0;
                        flipY = (index & 2) > 0;
                        continue;
                    }

                    if (fieldName == SharedData.ImageIndexState)
                    {
                        try
                        {
                            imageIndex = Convert.ToInt32( fieldInstance.__value );
                        }
                        catch (Exception)
                        {
                            imageIndex = null;
                        }
                        continue;
                    }

                    if ( !sourceProperties.TryGetValue( fieldName, out var value ) )
                    {
                        var field = GMProjectUtilities.EnumerateAllProperties( gmInstance.objectId ).FirstOrDefault( t => t.Property.varName == fieldName );
                        if ( field == null )
                        {
                            AnsiConsole.MarkupLineInterpolated( $"[red]There is no field with name [green]{fieldName}[/] in [teal]{gmInstance.objectId.name}[/][/]" );
                            continue;
                        }

                        if ( fieldDef.Meta == null )
                        {
                            entityType.CreateMetaFor( fieldDef, field.Property.varName );
                        }

                        value = new GMOverriddenProperty( field.Property, field.DefinedIn, gmInstance );
                        gmInstance.properties.Add( value );
                        AnsiConsole.MarkupLineInterpolated( $"Added field override [green]{value.varName}[/] in [teal]{gmInstance.objectId.name}[/] to [olive]{room.name}[/]" );
                    }

                    if ( fieldInstance.Meta == null )
                        instance.CreateMetaFor( fieldInstance, fieldName );
                    else
                        fieldInstance.Meta.GotError = false;

                    value.value = Field.MetaData.LDTK2GM( _ldtkProject, fieldInstance.__value, fieldDef.Meta?.type, fieldDef, value.propertyId, _entityLDTK2GM );
                }

                if (!gotFlipProperty)
                {
                    flipX = gmInstance.flipX;
                    flipY = gmInstance.flipY;
                }

                if ( flipX )
                    x -= (int) ((entityType.pivotX - 0.5f) * 2f * instance.width);
                if ( flipY )
                    y -= (int) ((entityType.pivotY - 0.5f) * 2f * instance.height);

                gmInstance.x = x;
                gmInstance.y = y;
                gmInstance.scaleX = flipX ? -scaleX : scaleX;
                gmInstance.scaleY = flipY ? -scaleY : scaleY;

                if (imageIndex != null)
                    gmInstance.imageIndex = imageIndex.Value;
            }
        }

        void ExportTiles( GMRTileLayer _tileLayer, IList<(Level.Layer layer, bool _initializingExisting)> _layersWithExtraData )
        {
            IList<Level.Layer> layers = _layersWithExtraData.Where( t => !t._initializingExisting ).Select( t => t.layer).ToList();
            if ( layers.Count == 0)
                return;

            var usedTileset = layers.Select( t => t.__tilesetDefUid ).FirstOrDefault( t => t != null );
            if ( usedTileset == null )
            {
                _tileLayer.tilesetId = null;
                return;
            }

            if ( layers.Count > 1 && !layers.All( t => t.__tilesetDefUid == null || t.__tilesetDefUid == usedTileset ) )
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

            var mainLayer = layers.First();

            if (layers.Count > 1 && !layers.All(t =>
                    t.pxOffsetX == mainLayer.pxOffsetX && t.pxOffsetY == mainLayer.pxOffsetY &&
                    t.__cWid == mainLayer.__cWid && t.__cHei == mainLayer.__cHei))
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Sizes for layers in group {_tileLayer.name} in level {_level.identifier} are different![/]");
            }

            _tileLayer.x = mainLayer.pxOffsetX;
            _tileLayer.y = mainLayer.pxOffsetY;

            uint[,] newData = new uint[mainLayer.__cWid, mainLayer.__cHei];

            var gmTilesetWidth = (gmTileset.spriteId.width - gmTileset.tilexoff + gmTileset.tilehsep) / (gmTileset.tileWidth + gmTileset.tilehsep);

            foreach (Level.Layer layer in layers.Reverse())
            {
                if ( layer.__tilesetDefUid == null)
                    continue;

                foreach (Level.Layer.TileInstance tile in layer.EnumerateAllTiles() )
                {
                    bool flipX = (tile.f & 2) > 0;
                    bool flipY = (tile.f & 1) > 0;

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
                        AnsiConsole.WriteException( new IndexOutOfRangeException( $"Out of range: {x}, {y} not in range {layer.__cWid}, {layer.__cHei}, level {_level.identifier}" ) );
                        continue;
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

        void ValidateRoomInstances( GMRoom _room )
        {
            var existingInstances = _room.layers.Where(t => t is GMRInstanceLayer).SelectMany(t => ((GMRInstanceLayer)t).instances).ToHashSet();

            ResourceList<GMRInstance>? list = _room.instanceCreationOrder;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var instance = list[i];
                if ( existingInstances.Contains( instance ) )
                    continue;
                existingInstances.Remove( instance );
            }

            existingInstances.ExceptWith(_room.instanceCreationOrder);

            foreach (GMRInstance instance in existingInstances )
            {
                list.Add(instance);
            }
        }

        void SortLayers(GMRoom _room)
        {
            List<string> orderInfo = new( _room.layers.Count );
            foreach (var layer in _level.layerInstances)
            {
                orderInfo.Add(layer.__identifier);
            }

            for (int i = 0; i < _room.layers.Count; i++)
            {
                var layer = _room.layers[i];
                if ( orderInfo.IndexOf(layer.name) > 0 )
                    continue;

                int insertAt = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    int layerIndex = orderInfo.IndexOf(_room.layers[j].name);
                    if ( layerIndex < 0 )
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
            } );
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
