using LDTK2GMS2Pipeline;
using LDTK2GMS2Pipeline.LDTK;
using ProjectManager;
using SixLabors.ImageSharp;
using Spectre.Console;
using System;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;

internal class Program
{
    const string LevelObjectTag = "Room Asset";
    const string IconFolder = "Generated";

    const string EntityAtlasName = "EntityAtlas";

    public static async Task Main( string[] args )
    {
        using var timer = TimerBenchmark.StartDebug( "TOTAL" );

        var ldtkProjectTask = LoadLDTKProject( false );
        await ldtkProjectTask;
        var gmProjectTask = YoYoProjectLoader.LoadGMProject();

        await Task.WhenAll( ldtkProjectTask, gmProjectTask );

        var ldtkProject = ldtkProjectTask.Result;
        var gmProject = gmProjectTask.Result;

        var levelObjects = gmProject
            .GetResourcesByType<GMObject>()
            .Cast<GMObject>()
            .Where( t => t.tags.Contains( LevelObjectTag ) )
            .ToList();

        var sprites = levelObjects
            .Where( t => t.spriteId != null )
            .Select( t => t.spriteId! )
            .Distinct()
            .ToList();

        AnsiConsole.Write( new Rule( "ATLAS" ) );

        string atlasPath = Path.Combine( ldtkProject.ProjectPath.DirectoryName!, IconFolder, $"{EntityAtlasName}.png" );
        SpriteAtlas atlas = new( atlasPath, ldtkProject.defaultGridSize );
        atlas.Add( sprites );

        bool atlasUpdated = await atlas.Update();

        var tileset = GetAtlasTileset( ldtkProject, atlasUpdated, atlas );

        if ( atlasUpdated )
            UpdateAtlasReferences( ldtkProject, atlas, tileset );

        AnsiConsole.Write( new Rule( "ENTITIES" ) );

        UpdateEntities( levelObjects, ldtkProject, atlas, tileset );

        AnsiConsole.Write( new Rule( "TILESETS" ) );

        await UpdateTilesets( ldtkProject, gmProject.GetResourcesByType<GMTileSet>().Cast<GMTileSet>().ToList() );

        AnsiConsole.Write( new Rule( "LEVELS" ) );

        UpdateLevels( gmProject, ldtkProject, gmProject.GetResourcesByType<GMRoom>().Cast<GMRoom>().ToList() );

        await ldtkProject.Save();
    }

    private static Task<LDTKProject> LoadLDTKProject( bool _loadDebugProject = false )
    {
        var files = IProjectUtilities.FindProjectFilesHere( ".ldtk" );
        FileInfo? ldtkProjectFile;
        if ( _loadDebugProject )
            files = files.Where( t => t.Name.Contains( "debug" ) );

        ldtkProjectFile = files.FirstOrDefault();

        if ( ldtkProjectFile is null )
            throw new Exception( "LDTK project not found" );

        return LDTKProject.Load( ldtkProjectFile );
    }

    private static async Task UpdateTilesets( LDTKProject _project, List<GMTileSet> _tilesets )
    {
        foreach ( GMTileSet gmTileset in _tilesets )
        {
            Tileset? tileset;
            if ( !_project.Meta.GetOrNew( gmTileset.name, out MetaData.TilesetInfo info ) )
            {
                tileset = _project.defs.tilesets.Find( t => t.identifier.Equals( gmTileset.name, StringComparison.CurrentCultureIgnoreCase ) );
                if ( tileset != null )
                {
                    AnsiConsole.MarkupLineInterpolated( $"Found a new tileset [teal]{gmTileset.name}[/]" );
                }
                else
                {
                    tileset = new Tileset()
                    {
                        uid = _project.GetNewUid(),
                        identifier = gmTileset.name
                    };
                    _project.defs.tilesets.Add( tileset );
                    AnsiConsole.MarkupLineInterpolated( $"Created a new tileset [teal]{gmTileset.name}[/]" );
                }

                info.uid = tileset.uid;
            }
            else
                tileset = _project.defs.tilesets.FirstOrDefault( t => t.uid == info.uid );

            if ( tileset == null )
                continue;

            if ( gmTileset.tilehsep != gmTileset.tilevsep )
                AnsiConsole.MarkupLineInterpolated( $"Error in [teal]{gmTileset.name}[/]! [red]Different spacing is not supported by LDTK: {gmTileset.tilehsep}, {gmTileset.tilevsep}[/]" );

            if ( gmTileset.tilexoff != gmTileset.tileyoff )
                AnsiConsole.MarkupLineInterpolated( $"Error in [teal]{gmTileset.name}[/]! [red]Different offsets are not supported by LDTK: {gmTileset.tilexoff}, {gmTileset.tileyoff}[/]" );

            if ( gmTileset.tilexoff != 0 || gmTileset.tileyoff != 0 )
                AnsiConsole.MarkupLineInterpolated( $"Warning in [teal]{gmTileset.name}[/]! [yellow]Offsets work as padding in LDTK. You may lose most right and bottom tiles![/]" );

            tileset.pxWid = gmTileset.tileWidth * gmTileset.out_columns;
            tileset.pxHei = gmTileset.tileHeight * gmTileset.tile_count / gmTileset.out_columns;
            tileset.tileGridSize = gmTileset.tileWidth;
            tileset.__cWid = gmTileset.out_columns;
            tileset.__cHei = gmTileset.tile_count / gmTileset.out_columns;
            tileset.spacing = gmTileset.tilehsep;
            tileset.padding = gmTileset.tilexoff;

            string tilesetFullPath = YoYoProjectLoader.GetFullPath( gmTileset.spriteId.GetCompositePaths()[0] );
            tileset.relPath = Path.GetRelativePath( _project.ProjectDirectory, tilesetFullPath );
        }
    }

    private static unsafe void UpdateLevels( GMProject _gmProject, LDTKProject _project, List<GMRoom> _rooms )
    {
        int GetMaxX()
        {
            return _project.levels.Max( t => t.worldX + t.pxWid );
        }

        Dictionary<GMObject, Entity> entityDict = _project.Meta.CreateMapping<GMObject, MetaData.ObjectInfo, Entity>(_gmProject);
        Dictionary<GMTileSet, Tileset> tilesetDict = _project.Meta.CreateMapping<GMTileSet, MetaData.TilesetInfo, Tileset>( _gmProject );

        foreach ( GMRoom room in _rooms )
        {
            if ( room.name != "rm_combat_test" )
                continue;

            LDTKProject.Level? level;
            if ( !_project.Meta.GetOrNew<MetaData.LevelInfo>( room.name, out var info ) )
            {
                level = new LDTKProject.Level
                {
                    uid = _project.GetNewUid(),
                    identifier = room.name
                };

                info.uid = level.uid;
                level.worldX = GetMaxX();

                _project.levels.Add( level );

                AnsiConsole.MarkupLineInterpolated( $"Created a new level [teal]{room.name}[/]" );
            }
            else
            {
                level = _project.levels.Find( t => t.uid == info.uid );
            }

            if ( level == null )
                continue;

            level.pxWid = room.roomSettings.Width;
            level.pxHei = room.roomSettings.Height;

            List<GMRLayer>? gmLayers = room.AllLayers();
            foreach ( Layer layerDef in _project.defs.layers )
            {
                var gmLayer = gmLayers.Find( t => t.name == layerDef.identifier );
                if ( gmLayer == null )
                    continue;

                string expectedLayerType;

                switch ( gmLayer )
                {
                    case GMRInstanceLayer:
                        expectedLayerType = "Entities";
                        break;

                    case GMRTileLayer:
                        expectedLayerType = "Tiles";
                        break;

                    default:
                        AnsiConsole.MarkupLineInterpolated( $"[yellow]Layer of type {gmLayer.GetType()} cannot be converted to {layerDef.type}[/]" );
                        continue;
                }

                if ( expectedLayerType != layerDef.type )
                {
                    AnsiConsole.MarkupLineInterpolated( $"[yellow]Layer types do not match for [green]{layerDef.identifier}[/] in [teal]{room.name}[/]: {expectedLayerType} vs {layerDef.type}[/]" );
                    continue;
                }

                Level.Layer? layer = level.layerInstances.Find( t => t.layerDefUid == layerDef.uid );
                if ( layer == null )
                {
                    layer = new()
                    {
                        __type = layerDef.__type,
                        __identifier = layerDef.identifier,
                        __gridSize = layerDef.gridSize,
                        iid = Guid.NewGuid().ToString(),
                        seed = Random.Shared.Next( 9999999 ),
                        __cWid = (room.roomSettings.Width + layerDef.gridSize - 1) / layerDef.gridSize,
                        __cHei = (room.roomSettings.Height + layerDef.gridSize - 1) / layerDef.gridSize,
                        levelId = level.uid,
                        layerDefUid = layerDef.uid
                    };

                    level.layerInstances.Add( layer );
                }

                layer.visible = gmLayer.visible;

                switch ( gmLayer )
                {
                    case GMRInstanceLayer instLayer:

                        layer.entityInstances.Clear();
                        foreach ( GMRInstance gmInstance in instLayer.instances )
                        {
                            if ( gmInstance.ignore || !entityDict.TryGetValue(gmInstance.objectId, out var entityType))
                                continue;

                            int posX = (int)gmInstance.x;
                            int posY = (int)gmInstance.y;

                            Level.Layer.EntityInstance instance = new();
        
                            instance.__identifier = entityType.identifier;
                            instance.defUid = entityType.uid;
                            instance.iid = Guid.NewGuid().ToString();
                            instance.__pivot = new List<double>() { entityType.pivotX, entityType.pivotY };
                            instance.__tags = entityType.tags;
                            instance.__tile = entityType.tileRect;
                            instance.px = new List<int>() { posX, posY };
                            instance.__grid = new List<int>() { posX / layerDef.gridSize, posY / layerDef.gridSize };
                            instance.__worldX = level.worldX + posX;
                            instance.__worldY = level.worldY + posY;
                            instance.width = (int) (entityType.width * MathF.Abs( gmInstance.scaleX ));
                            instance.height = (int) (entityType.height * MathF.Abs( gmInstance.scaleY ));

                            layer.entityInstances.Add( instance );
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

                        int gridSizeWithSpacing = (tileset.tileGridSize + tileset.spacing);

                        layer.__tilesetRelPath = tileset.relPath;
                        layer.__tilesetDefUid = tileset.uid;

                        layer.pxOffsetX = tileLayer.x;
                        layer.pxOffsetY = tileLayer.y;

                        var tilemap = tileLayer.tiles;
                        uint[,] tiles = tilemap.Tiles;

                        layer.gridTiles = new List<Level.Layer.TileInstance>(tilemap.Width * tilemap.Height);

                        for (int roomY = 0; roomY < tilemap.Height; roomY++)
                        for (int roomX = 0; roomX < tilemap.Width; roomX++)
                        {
                            var value = tilemap[roomX, roomY];

                            uint index = value & TileMap.TileBitMask_TileIndex;
                            if ( index == 0U )
                                continue;

                            bool flipX = TileMap.CheckBits( value, TileMap.TileBitMask_Flip );
                            bool flipY = TileMap.CheckBits( value, TileMap.TileBitMask_Mirror );
                            int tilesetX = (int) (index % tileset.__cWid);
                            int tilesetY = (int) (index / tileset.__cWid);
                            var tile = new Level.Layer.TileInstance();
                            tile.t = (int) index;
                            tile.f = (flipX ? 2 : 0) | (flipY ? 1 : 0);
                            tile.px = new int[] { roomX * layerDef.gridSize, roomY * layerDef.gridSize };
                            tile.src = new int[] { tileset.padding + tilesetX * gridSizeWithSpacing, tileset.padding + tilesetY * gridSizeWithSpacing };
                            tile.d = new int[] { roomX + roomY * tilemap.Width };

                            layer.gridTiles.Add( tile );
                        }


                        break;
                }

            }
        }
    }

    private static Tileset GetAtlasTileset( LDTKProject _ldtkProject, bool _atlasUpdated, SpriteAtlas _atlas )
    {
        var tileset = _ldtkProject.defs.tilesets.Find( t =>
            t.identifier.Equals( EntityAtlasName, StringComparison.InvariantCultureIgnoreCase ) );

        if ( !_atlasUpdated && tileset != null )
            return tileset;

        if ( tileset == null )
        {
            tileset = new LDTKProject.Tileset();
            _ldtkProject.defs.tilesets.Add( tileset );

            tileset.identifier = EntityAtlasName;
            tileset.relPath = $"{IconFolder}/{EntityAtlasName}.png";
            tileset.uid = _ldtkProject.GetNewUid();
        }

        tileset.pxWid = _atlas.Width;
        tileset.pxHei = _atlas.Height;
        tileset.tileGridSize = 16;
        tileset.__cWid = tileset.pxWid / tileset.tileGridSize;
        tileset.__cHei = tileset.pxHei / tileset.tileGridSize;
        tileset.cachedPixelData = new LDTKProject.Tileset.CachedPixelData();

        return tileset;
    }

    private static void UpdateAtlasReferences( LDTKProject _ldtkProject, SpriteAtlas _atlas, Tileset _atlasTileset )
    {
        foreach ( Entity entity in _ldtkProject.defs.entities )
        {
            if ( entity.tilesetId != _atlasTileset.uid || entity.tileRect == null )
                continue;

            var currentRect = new Rectangle( entity.tileRect.x, entity.tileRect.y, entity.tileRect.w, entity.tileRect.h );

            Rectangle? newRect = _atlas.UpdatePosition( currentRect );
            if ( newRect == null )
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

    private static void UpdateEntities( List<GMObject> _objects, LDTKProject _ldtkProject, SpriteAtlas _atlas, Tileset _atlasTileset )
    {
        static bool IsParentOf( GMObject _object, GMObject _parent )
        {
            GMObject? parentToCheck = _object.parentObjectId;
            while ( parentToCheck != null )
            {
                if ( parentToCheck == _parent )
                    return true;
                parentToCheck = parentToCheck.parentObjectId;
            }

            return false;
        }

        var sortedList = _objects.OrderBy( t => t.name ).ToList();
        sortedList.Sort( ( l, r ) =>
        {
            if ( IsParentOf( l, r ) )
                return 1;
            if ( IsParentOf( r, l ) )
                return -1;
            return 0;
        } );

        foreach ( var levelObject in _objects.OrderBy( t => t.name ) )
        {
            Entity? entity;
            if ( !_ldtkProject.Meta.GetOrNew<MetaData.ObjectInfo>( levelObject.name, out var objectInfo ) )
            {
                entity = _ldtkProject.defs.entities.Find( t =>
                    t.identifier.Equals( levelObject.name, StringComparison.InvariantCultureIgnoreCase ) );

                bool isNew = entity == null;
                if ( entity == null )
                {
                    entity = GM2LDTKUtilities.CreateEntity( _ldtkProject, levelObject, _atlasTileset, _atlas );
                    AnsiConsole.MarkupLineInterpolated( $"Created a new entity [teal]{entity.identifier}[/]" );
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated( $"Found a matching entity [teal]{entity.identifier}[/]" );
                }

                objectInfo.uid = entity.uid;

                GM2LDTKUtilities.UpdateEntity( _ldtkProject, levelObject, entity, isNew );

                continue;
            }

            // Do not recreate entities if they were removed in LDTK project later
            entity = _ldtkProject.defs.entities.Find( t => t.uid == objectInfo.uid );
            if ( entity != null )
                GM2LDTKUtilities.UpdateEntity( _ldtkProject, levelObject, entity, false );
        }

        _ldtkProject.Meta.RemoveMissing<MetaData.ObjectInfo>( _objects.Select( t => t.name ), _s =>
        {
            AnsiConsole.MarkupLineInterpolated( $"Object [teal]{_s}[/] no longer exists in the GM project. Removing from meta..." );
        } );
    }
}