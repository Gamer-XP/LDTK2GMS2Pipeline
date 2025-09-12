using JetBrains.Annotations;
using LDTK2GMS2Pipeline.Utilities;
using Microsoft.CSharp.RuntimeBinder;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Wrapper;

[UsedImplicitly]
public class GMProject_2024_1400 : GMProjectWrapper
{
    protected override async Task<GMProject> DoLoad( FileInfo _file, Action<float> _onProgressUpdate )
    {
        var loadingWait = new TaskCompletionSource<GMProject>();
        
        float progress = 0f;
        GMProject? result = null;
        
        dynamic project = new DynamicInvoker(typeof(ProjectInfo));
        Task task = project.LoadProjectOnTask(
            _file.FullName,
            true,
            null, 
            true,
            (Action<dynamic>) OnSuccess,
            (Action<float>) OnUpdate,
            (Action<dynamic>) OnFailure
            );

        await task;
        return await loadingWait.Task;
        
        void TryFinishTask()
        {
            if (progress >= 1f && result != null)
            {
                loadingWait.TrySetResult(result);
                result = null;
            }
        }

        void OnSuccess( dynamic _success )
        {
            result = (GMProject)_success.Resource;
            TryFinishTask();
        }

        void OnUpdate( float _fraction )
        {
            _onProgressUpdate?.Invoke(_fraction);
            progress = _fraction;
            TryFinishTask();
        }

        void OnFailure( dynamic _failure )
        {
            throw new Exception("Failed to load GameMaker project");
        }
    }

    protected override Task DoSave( GMProject _project )
    {
        var loadingWait = new TaskCompletionSource();
        
        Type? type = typeof(ProjectInfo).Assembly.GetType("YoYoStudio.Resources.ResourceBase+SaveParameters");
        if (type == null)
            throw new RuntimeBinderException();
        
        dynamic argument = new DynamicInvoker(Activator.CreateInstance(type));
        argument._onSuccess = (Action<ResourceBase>) OnSuccess;
        argument._onFailed = (Action<ResourceBase, Exception>) OnFailure;
        
        dynamic project = new DynamicInvoker(_project);
        project.Save(argument);

        return loadingWait.Task;

        void OnSuccess( ResourceBase _sender )
        {
            loadingWait.SetResult();
        }

        void OnFailure( ResourceBase _sender, Exception _ex )
        {
            loadingWait.SetException( new Exception("Failed to save GameMaker project") );
        }
    }
}