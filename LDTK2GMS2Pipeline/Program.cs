using System.ComponentModel;
using LDTK2GMS2Pipeline;
using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Sync;
using LDTK2GMS2Pipeline.Utilities;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Diagnostics;
using System.Reflection;
using YoYoStudio.Resources;

internal class Program
{
    private const bool LoadDebug = false;
    private const bool SaveDebug = false;
    private const bool NoSave = false;

    private const string DebugEnding = "_debug";

    private class AppOptions : CommandSettings
    {
        public enum Modes
        {
            Import,
            Export,
            ResizeTileset,
            ResetMeta
        }

        [CommandArgument(0, "<mode>")]
        [Description($"Mode in which application is executed." +
                     $"\n- [olive]{nameof(Modes.Import)}[/] - Imports data from GMS2 to LDTK" +
                     $"\n- [olive]{nameof(Modes.Export)}[/] - Exports data from LDTK to GMS2" +
                     $"\n- [olive]{nameof(Modes.ResizeTileset)}[/] - Fixes tile indexes in all rooms after resize" +
                     $"\n- [olive]{nameof(Modes.ResetMeta)}[/] - Allows to reset meta data for known objects, letting import process them again")
        ]
        public Modes Mode { get; set; } = Modes.Import;

        [CommandArgument(1, "[branch]")]
        [Description($"Which GM version is used to load the project. Options are: [olive]" +
                     $"\n- {nameof(GMBranch.Stable)}" +
                     $"\n- {nameof(GMBranch.Beta)}" +
                     $"\n- {nameof(GMBranch.LTS)}[/]" +
                     $"\nDefault is [olive]{nameof(GMBranch.Stable)}[/]")]
        [DefaultValue(GMBranch.Stable)]
        public GMBranch Branch { get; set; } = GMBranch.Stable;

        [CommandOption("-r|--reset_sprites")]
        [Description("Reset entities to reference their original tiles in the atlas. Valid in import mode only.")]
        public bool ForceUpdateAtlas { get; set; } = false;
    }

    private static void DrawLogo()
    {
        using var iconStream = typeof(Program).Assembly.GetManifestResourceStream("LDTK2GMS2Pipeline.Resources.GMIconTiny.png");
        if (iconStream == null)
            return;
        
        var img = new CanvasImage(iconStream);
        
        AnsiConsole.Write(
            new FigletText($"LDtk <> GMS2")
                .RightJustified());
        
        AnsiConsole.Cursor.SetPosition(0, 0);
        AnsiConsole.Write(img);
    }
    
    public static async Task Main( string[] _args )
    {
        if (_args.Length == 0)
            DrawLogo();
        
        var app = new CommandApp<App>();
        app.Configure( _config => _config.PropagateExceptions() );
        
        while (true)
        {
            if (_args.Length == 0) 
                _args = new [] { "-h" };

            try
            {
                var result = await app.RunAsync(_args);
                if (result > 0)
                    return;
            }
            catch (CommandParseException e)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{e.Message}[/]");
            }
            catch (CommandRuntimeException e)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{e.Message}[/]");
            }
            catch(Exception e)
            {
                AnsiConsole.WriteException(e);
            }

            var commandsLine = AnsiConsole.Ask<string>("Input commands: ");
            _args = commandsLine.Split(' ');
        }
    }
    
    private enum GMBranch
    {
        Stable,
        Beta,
        LTS
    }
    
    private class App : AsyncCommand<AppOptions>
    {
        public override async Task<int> ExecuteAsync(CommandContext _context, AppOptions _settings)
        {
            var timer = Stopwatch.StartNew();
            try
            {
                LoadAssemblies(GetInstallationPath(_settings.Branch));
                await HandleSuccess(_settings);
            }
            finally
            {
                AnsiConsole.MarkupLineInterpolated( $"[green]COMPLETE IN {timer.ElapsedMilliseconds} ms[/]" );
            }

            return 1;
        }
    }

    private static string? GetInstallationPath( GMBranch _installation )
    {
        //TODO: Different methods for MACOS and LINUX

        string registryFolder;
        switch (_installation)
        {
            case GMBranch.LTS:
                registryFolder = "GameMakerStudio2-LTS";
                break;
            case GMBranch.Beta:
                registryFolder = "GameMakerStudio2-Beta";
                break;
            default:
                registryFolder = "GameMakerStudio2";
                break;
        }

        var installDir = Microsoft.Win32.Registry.GetValue($"HKEY_CURRENT_USER\\SOFTWARE\\{registryFolder}", "Install_Dir", null) as string;
        return installDir;
    }
    
    private static void LoadAssemblies( string? _path )
    {
        _path ??= Directory.GetCurrentDirectory();

        AppDomain.CurrentDomain.AssemblyResolve += delegate(object _sender, ResolveEventArgs _args)
        {
            string assemblyFile = (_args.Name.Contains(','))
                ? _args.Name.Substring(0, _args.Name.IndexOf(','))
                : _args.Name;

            assemblyFile += ".dll";

            string targetPath = Path.Combine(_path, assemblyFile);

            try
            {
                return Assembly.LoadFile(targetPath);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]{ex.Message}[/]");
                return null;
            }
        };
    }

    static async Task HandleSuccess( AppOptions _options )
    {
        switch ( _options.Mode )
        {
            case AppOptions.Modes.Import:
            {
                var projects = await LoadProjects();
                await HandleImport(projects.ldtk, projects.gm, _options.ForceUpdateAtlas);
                break;
            }

            case AppOptions.Modes.Export:
            {
                var projects = await LoadProjects();
                await HandleExport(projects.ldtk, projects.gm);
                break;
            }

            case AppOptions.Modes.ResizeTileset:
            {
                var projects = await LoadProjects( false, true );
                await HandleResize(projects.gm);
                break;
            }

            case AppOptions.Modes.ResetMeta:
            {
                var projects = await LoadProjects();
                await HandleMetaReset(projects.ldtk, projects.gm);
                break;
            }

            default:
                throw new Exception("Unknown mode");
        }
    }

    static async Task<(LDTKProject ldtk, GMProject gm)> LoadProjects( bool _ldtk = true, bool _gm = true )
    {
        FileInfo? gmProjectFile = null, ldtkProjectFile = null;

        if (_gm)
        {
            gmProjectFile = FindProjectFile(".yyp");
            if (gmProjectFile is null)
                throw new FileNotFoundException($"Game Maker project file not found");
        }

        if (_ldtk)
        {
            ldtkProjectFile = FindProjectFile(".ldtk", _info => Path.GetFileNameWithoutExtension(_info.Name).EndsWith(DebugEnding) ^ !LoadDebug);
            if (ldtkProjectFile is null)
                throw new FileNotFoundException($"LDTK project file not found");
        }

        Task<LDTKProject> ldtkProjectTask = ldtkProjectFile != null? LDTKProject.Load( ldtkProjectFile ) : Task.FromResult<LDTKProject>(null!);
        Task<GMProject> gmProjectTask = gmProjectFile != null? GMProjectUtilities.LoadGMProject( gmProjectFile ) : Task.FromResult<GMProject>(null!);
        
        await Task.WhenAll( ldtkProjectTask, gmProjectTask );

        return (ldtkProjectTask.Result, gmProjectTask.Result);
    }

    static async Task HandleImport( LDTKProject _ldtkProject, GMProject _gmProject, bool _forceUpdateAtlas )
    {
        var atlas = await GM2LDTK.ImportToLDTK( _gmProject, _ldtkProject, _forceUpdateAtlas );
        
        if (NoSave)
            return;

        await atlas.SaveAtlas();

        await _ldtkProject.Save( SaveDebug ? DebugEnding : string.Empty );
    }

    static async Task HandleExport( LDTKProject _ldtkProject, GMProject _gmProject )
    {
        GMProjectUtilities.ResetDirtyAll( _gmProject );

        await LDTK2GM.ExportToGM( _gmProject, _ldtkProject );
        
        if (NoSave)
            return;

        await _ldtkProject.SaveMeta();

        await GMProjectUtilities.SaveGMProject( _gmProject );
    }

    static async Task HandleResize( GMProject _gmProject )
    {
        var options = new TilemapResizer.Options();

        AnsiConsole.Write(new Rule("[teal]Enter Tileset's name[/]"));
        AnsiConsole.WriteLine();
        AnsiConsole.Write( new Columns( _gmProject.GetResourcesByType<GMTileSet>().Select( t => t.name) ) );

        var name = AnsiConsole.Ask<string>("Enter Tileset Name:" );

        GMTileSet? tileSet = _gmProject.FindResourceByName(name, typeof(GMTileSet)) as GMTileSet;
        if (tileSet == null)
            throw new Exception($"Tileset {name} not found.");

        AnsiConsole.Write( new Rule( "[teal]Apply resize settings[/]" ) );

        TilemapResizer.GetTilesetSize(tileSet, out int columnsNow, out int rowsNow );

        bool alreadyResized = AnsiConsole.Ask<bool>("Is Tileset already resized?", true);
        if (alreadyResized)
        {
            options.ColumnsNew = columnsNow;
            options.RowsNew = rowsNow;
        }
        else
        {
            options.ColumnsPrevious = columnsNow;
            options.RowsPrevious = rowsNow;
        }

        AnsiConsole.MarkupLineInterpolated($"Current Size: {columnsNow}x{rowsNow}");

        if (alreadyResized)
        {
            options.ColumnsPrevious = AnsiConsole.Ask<int>("Enter Old Column Count:");
            options.RowsPrevious = AnsiConsole.Ask<int>( "Enter Old Row Count:" );
        }
        else
        {
            options.ColumnsNew = AnsiConsole.Ask<int>( "Enter New Column Count:" );
            options.RowsNew = AnsiConsole.Ask<int>( "Enter New Row Count:" );
        }

        options.AlignLeft = AnsiConsole.Ask<bool>("Align Left Side?", true);
        options.AlignTop = AnsiConsole.Ask<bool>( "Align Top Side?", true );
        options.OffsetX = AnsiConsole.Ask<int>( "Columns Offset", 0 );
        options.OffsetY = AnsiConsole.Ask<int>( "Rows Offset", 0 );

        GMProjectUtilities.ResetDirtyAll( _gmProject );

        TilemapResizer.Resize(_gmProject, tileSet, options );

        await GMProjectUtilities.SaveGMProject(_gmProject);
    }

    static async Task HandleMetaReset(LDTKProject _ldtk, GMProject _gm )
    {
        GMObject? obj;

        while (true)
        {
            var nm = AnsiConsole.Ask<string>("Enter entity name: ");
            obj = _gm.FindResourceByName(nm, typeof(GMObject)) as GMObject;
            if (obj != null)
                break;

            AnsiConsole.MarkupLineInterpolated($"[red]Unable to find object with name {nm}[/]");
        }

        GMObjectProperty? prop;
        while (true)
        {
            var fieldName = AnsiConsole.Ask<string>("Select what needs to be reset: enter property name or keyword 'all' for everything");
            if (fieldName == "all")
            {
                prop = null;
                break;
            }

            prop = obj.properties.Find(t => t.varName == fieldName);
            if (prop != null)
                break;
            
            AnsiConsole.MarkupLineInterpolated($"[red]Unable to find property with name {fieldName}[/]");
        }

        bool includeChildren = AnsiConsole.Ask<bool>("Including all child objects?");

        IEnumerable<LDTKProject.Entity> EnumerateAllEntities()
        {
            bool GetEntity( GMObject _object, out LDTKProject.Entity _entity )
            {
                var ent = _ldtk.GetMeta<LDTKProject.Entity.MetaData>(_object.name);
                if (ent != null && ent.Resource != null)
                {
                    _entity = ent.Resource;
                    return true;
                }

                _entity = null!;
                return false;
            }
            
            IEnumerable<GMObject> EnumerateWithChildren(GMObject _object)
            {
                yield return _object;

                foreach (GMObject gmObject in _object.childObjects)
                {
                    foreach (GMObject child in EnumerateWithChildren(gmObject))
                    {
                        yield return child;
                    }
                }
            }

            foreach (GMObject obj in EnumerateWithChildren(obj))
            {
                if (GetEntity(obj, out var ent))
                    yield return ent;
                
                if (!includeChildren)
                    break;
            }
        }

        var entityMetaList = _ldtk.GetMetaList<LDTKProject.Entity.MetaData>();
        foreach (var ent in EnumerateAllEntities())
        {
            if (prop == null)
            {
                entityMetaList.Remove(ent.Meta!);
                AnsiConsole.MarkupLineInterpolated($"Removed all meta for {ent.Meta!.identifier}");
            }
            else
            {
                var meta = ent.GetMeta<LDTKProject.Field.MetaData>(prop.varName);
                if (meta == null)
                    continue;
                var metaList = ent.GetMetaList<LDTKProject.Field.MetaData>();
                metaList.Remove(meta);
                AnsiConsole.MarkupLineInterpolated($"Removed {meta.identifier} from {ent.Meta!.identifier}");
            }
        }

        if (NoSave)
            return;

        await _ldtk.SaveMeta();
    }

    private static FileInfo? FindProjectFile( string _extension, Func<FileInfo, bool>? _filter = null )
    {
        DirectoryInfo? projectPath = new DirectoryInfo( Directory.GetCurrentDirectory() );

        while ( projectPath is { Exists: true } )
        {
            foreach ( FileInfo file in projectPath.EnumerateFiles() )
            {
                if ( file.Extension == _extension && (_filter == null || _filter( file )) )
                    return file;
            }

            projectPath = projectPath.Parent;
        }

        return null;
    }

}