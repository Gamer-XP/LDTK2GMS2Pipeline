using LDTK2GMS2Pipeline.Utilities;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Wrapper;

public abstract class GMProjectWrapper
{
    public async Task<GMProject> Load( FileInfo _file, Action<float> _onProgressUpdate )
    {
        var project = await DoLoad( _file, _onProgressUpdate );
        GMProjectUtilities.SetProjectPath(project, _file.FullName);
        return project;
    }

    public Task Save( GMProject _project )
    {
        return DoSave( _project );
    }
    
    protected abstract Task<GMProject> DoLoad( FileInfo _file, Action<float> _onProgressUpdate );
    protected abstract Task DoSave( GMProject _project );
}