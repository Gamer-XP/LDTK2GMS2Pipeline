using System.Diagnostics;
using System.Reflection;
using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Sync;
using Spectre.Console;
using CommandLine;
using YoYoStudio.Resources;
using LDTK2GMS2Pipeline.Utilities;

internal class Program
{
    private const bool LoadDebug = true;
    private const bool SaveDebug = false;

    private const string DebugEnding = "_debug";

    private class Options
    {
        [Option("export", Default = false, HelpText = "Switch to exporting data back to GM")]
        public bool IsExport { get; set; }
    }

    public static async Task Main( string[] _args )
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await Parser.Default.ParseArguments<Options>( _args ).WithNotParsed( HandleParseError ).WithParsedAsync( HandleSuccess );
        }
        finally
        {
            AnsiConsole.MarkupLineInterpolated( $"[green]COMPLETE IN {timer.ElapsedMilliseconds / 1000L}[/]" );
        }
    }

    static async Task HandleSuccess( Options _options)
    {
        var gmProjectFile = FindProjectFile(".yyp");
        var ldtkProjectFile = FindProjectFile(".ldtk", _info => Path.GetFileNameWithoutExtension(_info.Name).EndsWith(DebugEnding) ^ LoadDebug);

        if (gmProjectFile is null)
            throw new FileNotFoundException($"Game Maker project file not found");

        if (ldtkProjectFile is null)
            throw new FileNotFoundException($"LDTK project file not found");

        var ldtkProjectTask = LDTKProject.Load( ldtkProjectFile );
        var gmProjectTask = GMProjectUtilities.LoadGMProject( gmProjectFile  );

        await Task.WhenAll( ldtkProjectTask, gmProjectTask );

        var ldtkProject = ldtkProjectTask.Result;
        var gmProject = gmProjectTask.Result;

        if ( !_options.IsExport )
        {
            await GM2LDTK.ImportToLDTK( gmProject, ldtkProject );

            await ldtkProject.Save( SaveDebug ? DebugEnding : string.Empty );
        }
        else
        {
            GMProjectUtilities.ResetDirty(gmProject);
            foreach (var resource in gmProject.resources.Select( t => t.id)) 
                GMProjectUtilities.ResetDirty(resource);

            await LDTK2GM.ExportToGM(gmProject, ldtkProject);

            await GMProjectUtilities.SaveGMProject(gmProject);
        }
    }

    static void HandleParseError( IEnumerable<Error> _errs )
    {
        foreach (Error err in _errs) 
            AnsiConsole.MarkupLineInterpolated($"[red]{err}[/]");
    }

    private static FileInfo? FindProjectFile( string _extension, Func<FileInfo, bool>? _filter = null)
    {
        DirectoryInfo? projectPath = new DirectoryInfo( Directory.GetCurrentDirectory() );

        while ( projectPath is { Exists: true } )
        {
            foreach ( FileInfo file in projectPath.EnumerateFiles() )
            {
                if (file.Extension == _extension && (_filter == null || _filter(file)))
                    return file;
            }

            projectPath = projectPath.Parent;
        }

        return null;
    }

}