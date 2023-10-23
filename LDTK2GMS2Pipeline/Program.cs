using LDTK2GMS2Pipeline;
using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Sync;
using ProjectManager;
using Spectre.Console;
using CommandLine;

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
        using var timer = TimerBenchmark.StartDebug( "TOTAL" );

        await Parser.Default.ParseArguments<Options>(_args).WithNotParsed(HandleParseError).WithParsedAsync(HandleSuccess);
    }

    static async Task HandleSuccess( Options _options)
    {
        var ldtkProjectTask = LDTKProject.Load( LoadDebug? DebugEnding : null );
        var gmProjectTask = GMProjectUtilities.LoadGMProject();

        await Task.WhenAll( ldtkProjectTask, gmProjectTask );

        var ldtkProject = ldtkProjectTask.Result;
        var gmProject = gmProjectTask.Result;

        if ( !_options.IsExport )
        {
            await LDTKImporter.ImportToLDTK( gmProject, ldtkProject );

            await ldtkProject.Save( SaveDebug ? DebugEnding : string.Empty );
        }
        else
        {
            await LDTKExporter.ExportToGM(gmProject, ldtkProject);

            await GMProjectUtilities.SaveGMProject(gmProject);
        }
    }

    static void HandleParseError( IEnumerable<Error> _errs )
    {
        foreach (Error err in _errs) 
            AnsiConsole.MarkupLineInterpolated($"[red]{err}[/]");
    }

}