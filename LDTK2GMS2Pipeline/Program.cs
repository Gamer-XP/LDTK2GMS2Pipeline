using LDTK2GMS2Pipeline;
using LDTK2GMS2Pipeline.LDTK;
using ProjectManager;
using SixLabors.ImageSharp;
using Spectre.Console;
using System;
using System.Collections.Generic;
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
        var gmProjectTask = GMProjectUtilities.LoadGMProject();

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

        Tileset tileset = GetAtlasTileset( ldtkProject, atlasUpdated, atlas );

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
            _project.CreateOrExisting<Tileset>(gmTileset.name, out var tileset);
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

            string tilesetFullPath = GMProjectUtilities.GetFullPath( gmTileset.spriteId.GetCompositePaths()[0] );
            tileset.relPath = Path.GetRelativePath( _project.ProjectDirectory, tilesetFullPath );
        }
    }

    private static void UpdateLevels( GMProject _gmProject, LDTKProject _project, List<GMRoom> _rooms )
    {
        int GetMaxX()
        {
            return _project.levels.Max( t => t.worldX + t.pxWid );
        }

        var entityDict = _project.CreateResourceMap<GMObject, Entity>(_gmProject);
        var tilesetDict = _project.CreateResourceMap<GMTileSet, Tileset>( _gmProject );

        var flipEnum = LDTKProject.GetFlipEnum(_project);

        foreach ( GMRoom room in _rooms )
        {
            if ( room.name != "rm_combat_test" )
                continue;

            if (_project.CreateOrExisting<Level>(room.name, out var level))
            {
                level.worldX = GetMaxX();
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
                    AnsiConsole.MarkupLineInterpolated( $"[yellow]Layer types do not match for [green]{layerDef.identifier}[/] in [olive]{room.name}[/]: {expectedLayerType} vs {layerDef.type}[/]" );
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
                            if ( gmInstance.ignore)
                                continue;

                            if (!entityDict.TryGetValue(gmInstance.objectId, out var entityData))
                            {
                                AnsiConsole.MarkupLineInterpolated($"[yellow]Unable to find unity matching object [teal]{gmInstance.objectId.name}[/] for level [olive]{room.name}[/][/]");
                                continue;
                            }

                            var entityType = entityData;

                            bool GetField( string _name, out Field.MetaData _meta )
                            {
                                return entityData.Meta.Properties.TryGetValue(_name, out _meta!);
                            }

                            int width = (int)(entityType.width * MathF.Abs(gmInstance.scaleX));
                            int height = (int)(entityType.height * MathF.Abs(gmInstance.scaleY));
                            int posX = (int) gmInstance.x;
                            int posY = (int) gmInstance.y;

                            Level.Layer.EntityInstance instance = new();
                            instance.__pivot = new List<double>() { entityType.pivotX, entityType.pivotY };

                            if ( gmInstance.flipX )
                                posX += (int)((entityType.pivotX - 0.5f) * 2f * width);
                            if ( gmInstance.flipY )
                                posY += (int) ((entityType.pivotY - 0.5f) * 2f * height);

                            instance.__identifier = entityType.identifier;
                            instance.defUid = entityType.uid;
                            instance.iid = Guid.NewGuid().ToString();
                            
                            instance.__tags = entityType.tags;
                            instance.__tile = entityType.tileRect;
                            instance.px = new List<int>() { posX, posY };
                            instance.__grid = new List<int>() { posX / layerDef.gridSize, posY / layerDef.gridSize };
                            instance.__worldX = level.worldX + posX;
                            instance.__worldY = level.worldY + posY;
                            instance.width = width;
                            instance.height = height;

                            int flipIndex = (gmInstance.flipX ? 1 : 0) | (gmInstance.flipY ? 2 : 0);

                            if (flipIndex > 0)
                            {
                                if ( GetField( LDTKProject.FlipStateEnumName,  out var fieldMeta ))
                                    instance.fieldInstances.Add(new Level.Layer.EntityInstance.FieldInstance( fieldMeta.Resource, DefaultOverride.IdTypes.V_String, flipEnum.values[flipIndex].id ));
                            }

                            foreach (GMOverriddenProperty propOverride in gmInstance.properties)
                            {
                                if ( !GetField( propOverride.varName, out var meta ) )
                                    continue;

                                if (!LDTKProject.ConvertDefaultValue(propOverride.propertyId, propOverride.value, out var result, meta.type) )
                                {
                                    AnsiConsole.MarkupLineInterpolated( $"[red]Error processing value '{propOverride.value}' for field [green]{propOverride.varName} [[{propOverride.propertyId.varType}]][/] in [teal]{gmInstance.objectId.name}[/].[/]" );
                                    continue;
                                }

                                var field = new Level.Layer.EntityInstance.FieldInstance(meta.Resource);
                                field.SetValue( result );
                                instance.fieldInstances.Add( field );
                            }

                            layer.entityInstances.Add( instance );
                        }

                        break;

                    case GMRTileLayer tileLayer:

                        Tileset? tileset = tilesetDict.GetValueOrDefault(tileLayer.tilesetId);

                        if (tileset == null)
                        {
                            layer.__tilesetRelPath = null;
                            layer.__tilesetDefUid = null;
                            break;
                        }

                        int gridSizeWithSpacing = (tileset.tileGridSize + tileset.spacing);

                        layer.__tilesetRelPath = tileset.relPath;
                        layer.__tilesetDefUid = tileset.uid;
                        layer.overrideTilesetUid = tileset.uid;

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
        _atlasUpdated |= _ldtkProject.CrateOrExistingForced(EntityAtlasName, out Tileset tileset);

        if ( !_atlasUpdated )
            return tileset;

        tileset.relPath = $"{IconFolder}/{EntityAtlasName}.png";
        tileset.pxWid = _atlas.Width;
        tileset.pxHei = _atlas.Height;
        tileset.tileGridSize = 16;
        tileset.__cWid = tileset.pxWid / tileset.tileGridSize;
        tileset.__cHei = tileset.pxHei / tileset.tileGridSize;

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
        var sortedList = _objects.OrderBy( t => t.name ).ToList();
        sortedList.Sort( ( l, r ) =>
        {
            if ( GMProjectUtilities.IsInheritedFrom( l, r ) )
                return 1;
            if ( GMProjectUtilities.IsInheritedFrom( r, l ) )
                return -1;
            return 0;
        } );

        foreach ( var levelObject in _objects.OrderBy( t => t.name ) )
        {
            bool isNew = _ldtkProject.CreateOrExisting(levelObject.name, out Entity ? entity );

            if (entity != null)
            {
                if (isNew)
                    entity.Init( levelObject, _atlasTileset, _atlas );
                _ldtkProject.UpdateEntity(entity, levelObject, isNew);
            }
        }

        _ldtkProject.RemoveUnusedMeta<Entity.MetaData>( _objects.Select( t => t.name ), _s =>
        {
            AnsiConsole.MarkupLineInterpolated( $"Object [teal]{_s}[/] no longer exists in the GM project. Removing from meta..." );
        } );
    }
}