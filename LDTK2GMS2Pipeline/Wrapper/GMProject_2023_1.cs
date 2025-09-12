using JetBrains.Annotations;
using LDTK2GMS2Pipeline.Utilities;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Wrapper;

[UsedImplicitly]
public class GMProject_2023_1 : GMProjectWrapper
{
    protected override Task<GMProject> DoLoad( FileInfo _file, Action<float> _onProgressUpdate )
    {
        var loadingWait = new TaskCompletionSource<GMProject>();
        
        float progress = 0f;
        GMProject? result = null;

        dynamic getter = new DynamicInvoker(typeof(ProjectInfo));
        getter.LoadProject(
            _file.FullName, 
            true, 
            (Action<dynamic>) OnSuccess, 
            (Action<dynamic, float>) OnUpdate, 
            (Action<dynamic>)OnFailure);
        
        return loadingWait.Task;
        
        void TryFinishTask()
        {
            if (progress >= 1f && result != null)
            {
                loadingWait.TrySetResult(result);
                result = null;
            }
        }

        void OnSuccess( dynamic _r )
        {
            result = (GMProject)_r;
            TryFinishTask();
        }

        void OnUpdate( dynamic _r, float _progress )
        {
            _onProgressUpdate?.Invoke(_progress);
            progress = _progress;
            TryFinishTask();
        }

        void OnFailure( dynamic _r )
        {
            throw new Exception("Failed to load GameMaker project");
        }
    }

    protected override Task DoSave( GMProject _project )
    {
        var loadingWait = new TaskCompletionSource();
        
        dynamic project = new DynamicInvoker(_project);
        project.Save((Action<dynamic>) OnSuccess, (Action<dynamic, float>)OnUpdate, (Action<dynamic>) OnFailure);

        return loadingWait.Task;

        void OnSuccess( dynamic _sender )
        {
            loadingWait.SetResult();
        }

        void OnUpdate( dynamic _sender, float _progress )
        {
            
        }

        void OnFailure( dynamic _sender )
        {
            loadingWait.SetException( new Exception("Failed to save GameMaker project") );
        }
    }
}