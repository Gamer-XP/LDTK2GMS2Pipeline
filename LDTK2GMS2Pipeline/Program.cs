using CommandLine;
using LDTK2GMS2Pipeline;
using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Sync;
using LDTK2GMS2Pipeline.Utilities;
using Spectre.Console;
using System.Diagnostics;
using CommandLine.Text;
using YoYoStudio.Resources;

internal class Program
{
    private const bool LoadDebug = false;
    private const bool SaveDebug = false;
    private const bool NoSave = false;

    private const string DebugEnding = "_debug";

    private class AppOptions
    {
        public enum Modes
        { 
            Import,
            Export,
            ResizeTileset
        }

        [Option("mode", HelpText = $"Mode at which application executed." +
                                   $"\n- {nameof(Modes.Import)} - Imports data from GMS2 to LDTK" +
                                   $"\n- {nameof(Modes.Export)} - Exports data from LDTK to GMS2" +
                                   $"\n- {nameof(Modes.ResizeTileset)} - Fixes tile indexes in all rooms after resize")]
        public Modes? Mode { get; set; } = null;

        [Option( "reset_sprites", Default = false, HelpText = "Reset entities to reference their original tiles in the atlas. Valid in import mode only." )]
        public bool ForceUpdateAtlas { get; set; }
    }

    public static async Task Main( string[] _args )
    {
        while (true)
        {
            var parsed = Parser.Default.ParseArguments<AppOptions>(_args);
            if (parsed is NotParsed<AppOptions> errors)
            {
                HandleError(errors.Errors);
            }
            else
            {
                if (parsed.Value.Mode != null)
                {
                    var timer = Stopwatch.StartNew();
                    try
                    {
                        await HandleSuccess(parsed.Value);
                    }
                    finally
                    {
                        AnsiConsole.MarkupLineInterpolated( $"[green]COMPLETE IN {timer.ElapsedMilliseconds} ms[/]" );
                    }
                    return;
                }

                AnsiConsole.WriteLine(HelpText.AutoBuild(parsed, t => t, e => e));
            }

            var commands = AnsiConsole.Ask<string>("Input commands: ");
            _args = commands.Split(' ');
        }
    }

    static async Task HandleSuccess( AppOptions _options )
    {
        if (_options.Mode == null)
        {
            AnsiConsole.WriteLine(HelpText.AutoBuild(Parser.Default.ParseArguments<AppOptions>(Array.Empty<string>()), h => h, e => e));
            _options.Mode = AnsiConsole.Ask<AppOptions.Modes>("Input mode");
        }
        
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
                var projects = await LoadProjects();
                await HandleResize(projects.gm);
                break;
            }

            default:
                throw new Exception("Unknown mode");
        }
    }

    static async Task<(LDTKProject ldtk, GMProject gm)> LoadProjects()
    {
        var gmProjectFile = FindProjectFile( ".yyp" );
        var ldtkProjectFile = FindProjectFile( ".ldtk", _info => Path.GetFileNameWithoutExtension( _info.Name ).EndsWith( DebugEnding ) ^ !LoadDebug );

        if ( gmProjectFile is null )
            throw new FileNotFoundException( $"Game Maker project file not found" );

        if ( ldtkProjectFile is null )
            throw new FileNotFoundException( $"LDTK project file not found" );

        Task<LDTKProject> ldtkProjectTask = LDTKProject.Load( ldtkProjectFile );
        Task<GMProject> gmProjectTask = GMProjectUtilities.LoadGMProject( gmProjectFile );

        await Task.WhenAll( ldtkProjectTask, gmProjectTask );

        var ldtkProject = ldtkProjectTask.Result;
        var gmProject = gmProjectTask.Result;

        return (ldtkProject, gmProject);
    }

    static async Task HandleImport( LDTKProject _ldtkProject, GMProject _gmProject, bool _forceUpdateAtlas )
    {
        await GM2LDTK.ImportToLDTK( _gmProject, _ldtkProject, _forceUpdateAtlas );
        
        if (NoSave)
            return;

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

    static void HandleError( IEnumerable<Error> _errs )
    {
        foreach ( Error err in _errs )
            AnsiConsole.MarkupLineInterpolated( $"[red]{err}[/]" );
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