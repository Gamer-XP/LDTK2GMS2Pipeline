using LDTK2GMS2Pipeline;
using LDTK2GMS2Pipeline.LDTK;
using ProjectManager;
using SixLabors.ImageSharp;
using System.Diagnostics;
using Spectre.Console;
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

        var ldtkProjectTask = LoadLDTKProject(true);
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

        string atlasPath = Path.Combine(ldtkProject.ProjectPath.DirectoryName!, IconFolder, $"{EntityAtlasName}.png");
        SpriteAtlas atlas = new ( atlasPath, ldtkProject.defaultGridSize );
        atlas.Add( sprites )
            ;
        bool atlasUpdated = await atlas.Update();

        var tileset = GetAtlasTileset( ldtkProject, atlasUpdated, atlas );

        if ( atlasUpdated )
            UpdateAtlasReferences( ldtkProject, atlas, tileset );

        UpdateEntities( levelObjects, ldtkProject, atlas, tileset );

        await ldtkProject.Save();
    }

    private static Task<LDTKProject> LoadLDTKProject( bool _loadDebugProject = false )
    {
        var files = IProjectUtilities.FindProjectFilesHere(".ldtk");
        FileInfo? ldtkProjectFile;
        if (_loadDebugProject)
            files = files.Where(t => t.Name.Contains("debug"));

        ldtkProjectFile = files.FirstOrDefault();

        if ( ldtkProjectFile is null )
            throw new Exception( "LDTK project not found" );

        return LDTKProject.Load( ldtkProjectFile );
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

    private static void UpdateAtlasReferences(LDTKProject _ldtkProject, SpriteAtlas _atlas, Tileset _atlasTileset )
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

    private static void UpdateEntities( List<GMObject> _objects, LDTKProject _ldtkProject, SpriteAtlas _atlas, Tileset _atlasTileset )
    {
        static bool IsParentOf( GMObject _object, GMObject _parent )
        {
            GMObject? parentToCheck = _object.parentObjectId;
            while ( parentToCheck != null)
            {
                if (parentToCheck == _parent )
                    return true;
                parentToCheck = parentToCheck.parentObjectId;
            }

            return false;
        }

        var sortedList = _objects.OrderBy(t => t.name).ToList();
        sortedList.Sort((l, r) =>
        {
            if (IsParentOf(l, r))
                return 1;
            if (IsParentOf(r, l))
                return -1;
            return 0;
        });

        foreach ( var levelObject in _objects.OrderBy( t => t.name ) )
        {
            Entity? entity;
            var objectInfo = _ldtkProject.Meta.ObjectGet(levelObject.name);
            if ( objectInfo == null )
            {
                entity = _ldtkProject.defs.entities.Find(t =>
                    t.identifier.Equals(levelObject.name, StringComparison.InvariantCultureIgnoreCase));

                bool isNew = entity == null;
                if (entity == null)
                {
                    entity = GM2LDTKUtilities.CreateEntity( _ldtkProject, levelObject, _atlasTileset, _atlas);
                    AnsiConsole.MarkupLineInterpolated( $"Created a new entity [teal]{entity.identifier}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLineInterpolated( $"Found a matching entity [teal]{entity.identifier}[/]" );
                }

                _ldtkProject.Meta.ObjectAdd( entity.identifier, new MetaData.ObjectInfo() { uid = entity.uid } );

                GM2LDTKUtilities.UpdateEntity( _ldtkProject, levelObject, entity, isNew );

                continue;
            }

            // Do not recreate entities if they were removed in LDTK project later
            entity = _ldtkProject.defs.entities.Find( t => t.uid == objectInfo.uid);
            if (entity != null)
                GM2LDTKUtilities.UpdateEntity( _ldtkProject, levelObject, entity,false );
        }

        var removedObjectNames = _ldtkProject.Meta.Objects.Keys.Except( _objects.Select( t => t.name ), StringComparer.InvariantCultureIgnoreCase ).ToList();
        foreach (string obj in removedObjectNames )
        {
            _ldtkProject.Meta.Objects.Remove(obj);
            AnsiConsole.WriteLine($"Object {obj} no longer exists in the GM project. Removing from meta...");
        }
    }

    

    private static bool WasReferencingAtlasItem( LDTKProject.Entity _entity, SpriteAtlas.IAtlasItem _item )
    {
        var entityRect = _entity.tileRect;
        var atlasRect = _item.PreviousRectangle;

        Debug.Assert( entityRect != null );
        Debug.Assert( atlasRect != null );

        bool isMatching = entityRect.x == atlasRect.X &&
                          entityRect.y == atlasRect.Y &&
                          entityRect.w == atlasRect.Width &&
                          entityRect.h == atlasRect.Height;

        return isMatching;
    }
}