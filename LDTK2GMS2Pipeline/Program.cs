using CommandLine;
using LDTK2GMS2Pipeline;
using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Sync;
using LDTK2GMS2Pipeline.Utilities;
using Spectre.Console;
using System.Diagnostics;
using YoYoStudio.Resources;

internal class Program
{
    private const bool LoadDebug = false;
    private const bool SaveDebug = false;

    private const string DebugEnding = "_debug";

    private class AppOptions
    {
        public enum Modes
        { 
            Import,
            Export,
            ResizeTileset
        }

        [Option( "mode", Default = Modes.Import, HelpText = $"Mode at which application executed. {nameof( Modes.Import )}, {nameof( Modes.Export )}, {nameof( Modes.ResizeTileset )}" )]
        public Modes Mode { get; set; }

        [Option( "reset_sprites", Default = false, HelpText = "Reset entities to reference their original tiles in the atlas. Valid in import mode only." )]
        public bool ForceUpdateAtlas { get; set; }
    }

    public static async Task Main( string[] _args )
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await Parser.Default.ParseArguments<AppOptions>( _args ).WithNotParsed( HandleParseError ).WithParsedAsync( HandleSuccess );
        }
        finally
        {
            AnsiConsole.MarkupLineInterpolated( $"[green]COMPLETE IN {timer.ElapsedMilliseconds} ms[/]" );
        }
    }

    static async Task HandleSuccess( AppOptions _options )
    {
        var gmProjectFile = FindProjectFile( ".yyp" );
        var ldtkProjectFile = FindProjectFile( ".ldtk", _info => Path.GetFileNameWithoutExtension( _info.Name ).EndsWith( DebugEnding ) ^ !LoadDebug );

        if ( gmProjectFile is null )
            throw new FileNotFoundException( $"Game Maker project file not found" );

        if ( ldtkProjectFile is null )
            throw new FileNotFoundException( $"LDTK project file not found" );

        var ldtkProjectTask = LDTKProject.Load( ldtkProjectFile );
        var gmProjectTask = GMProjectUtilities.LoadGMProject( gmProjectFile );

        await Task.WhenAll( ldtkProjectTask, gmProjectTask );

        var ldtkProject = ldtkProjectTask.Result;
        var gmProject = gmProjectTask.Result;

        switch ( _options.Mode )
        {
            case AppOptions.Modes.Import:
                await HandleImport( ldtkProject, gmProject, _options.ForceUpdateAtlas );
                break;

            case AppOptions.Modes.Export:
                await HandleExport( ldtkProject, gmProject );
                break;

            case AppOptions.Modes.ResizeTileset:
                await HandleResize( gmProject );
                break;

            default:
                throw new Exception("Unknown mode");
        }
    }

    static async Task HandleImport( LDTKProject _ldtkProject, GMProject _gmProject, bool _forceUpdateAtlas )
    {
        await GM2LDTK.ImportToLDTK( _gmProject, _ldtkProject, _forceUpdateAtlas );

        await _ldtkProject.Save( SaveDebug ? DebugEnding : string.Empty );
    }

    static async Task HandleExport( LDTKProject _ldtkProject, GMProject _gmProject )
    {
        GMProjectUtilities.ResetDirtyAll( _gmProject );

        await LDTK2GM.ExportToGM( _gmProject, _ldtkProject );

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

    static void HandleParseError( IEnumerable<Error> _errs )
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