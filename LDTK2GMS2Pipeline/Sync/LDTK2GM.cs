using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Utilities;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;

namespace LDTK2GMS2Pipeline.Sync;

internal partial class LDTK2GM
{
    public static async Task ExportToGM( GMProject _gmProject, LDTKProject _ldtkProject )
    {
        //ProjectInfo.IsLoading = false;

        var matches = InitializeEntityMatches(_gmProject, _ldtkProject);
        
        Log.PushTitle("LEVELS");

        foreach ( Level level in _ldtkProject.levels )
        {
            ExportLevel( _gmProject, _ldtkProject, level, matches );
        }
        
        Log.PopTitle();
    }
}
