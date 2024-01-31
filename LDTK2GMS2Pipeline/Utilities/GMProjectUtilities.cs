using System.Diagnostics;
using System.Reflection;
using Spectre.Console;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Utilities;

public static class GMProjectUtilities
{
    public static Task<GMProject> LoadGMProject(FileInfo _file)
    {
        Debug.Assert(_file != null, "GameMaker project file not found");

        FileIO.SetDefaultFileFunctions();
        MessageIO.SetDefaultMessageFunctions();
        ResourceInfo.FindAllResources();
        GMProject.LicenseModules = new PlaceholderLicensingModule();

        var loadingWait = new TaskCompletionSource<GMProject>();
        
        float progress = 0f;
        GMProject? result = null;

        void TryFinishTask()
        {
            if (progress >= 1f && result != null)
            {
                Console.WriteLine();
                loadingWait.TrySetResult(result);
                result = null;
            }
        }

        string GenerateProgressLine( float _progress )
        {
            int border = (int)(_progress * 10);
            return $"\rLoading: [{ string.Concat( Enumerable.Range(1, 10).Select( i => i <= border? '-' : ' ' ) ) }] { Math.Round(_progress * 100) }%";
        }
        
        ProjectInfo.LoadProject(_file.FullName, true, (_r) =>
        {
            result = (GMProject)_r;
            TryFinishTask();
            AnsiConsole.MarkupLineInterpolated($"Loaded project {_r.name}");
        }, (_r, _progress) =>
        {
            var pos = Console.GetCursorPosition();
            Console.Write(GenerateProgressLine(_progress));
            Console.SetCursorPosition(0, pos.Top);
            
            progress = _progress;
            TryFinishTask();
        }, (_r) =>
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Failed to load {_r.name}[/]");
            throw new Exception("Failed to load GameMaker project");
        });

        return loadingWait.Task;
    }

    public static Task SaveGMProject(GMProject _project)
    {
        if (!_project.isDirty)
            return Task.CompletedTask;

        var loadingWait = new TaskCompletionSource();

        _project.Save(_sender =>
            {
                Console.WriteLine($"Saved: {_sender.name}");
                if (_sender == _project)
                    loadingWait.SetResult();
            }, (_sender, _progress) =>
            {

            },
            _sender =>
            {
                loadingWait.SetException(new Exception($"Failed to save: {_sender.name}"));
            });


        return loadingWait.Task;
    }

    public static string GetFullPath(string _path, GMProject? _project = null)
    {
        return Path.Combine(Path.GetDirectoryName(ProjectInfo.GetProjectPath(_project ?? ProjectInfo.Current)), _path);
    }

    /// <summary>
    /// Returns whole inheritance hierarchy for given object
    /// </summary>
    public static IEnumerable<GMObject> EnumerateObjectHierarchy(GMObject _object)
    {
        GMObject? obj = _object;
        while (obj != null)
        {
            yield return obj;
            obj = obj.parentObjectId;
        }
    }

    public static bool IsInheritedFrom(GMObject _object, GMObject _parent)
    {
        GMObject? parentToCheck = _object.parentObjectId;
        while (parentToCheck != null)
        {
            if (parentToCheck == _parent)
                return true;
            parentToCheck = parentToCheck.parentObjectId;
        }

        return false;
    }

    public record GMObjectPropertyInfo(GMObjectProperty Property, GMObject DefinedIn);

    /// <summary>
    /// Returns all properties that given object has, including object they are defined in
    /// </summary>
    public static IEnumerable<GMObjectPropertyInfo> EnumerateAllProperties(GMObject _object)
    {
        foreach (GMObject obj in EnumerateObjectHierarchy(_object))
        {
            foreach (GMObjectProperty property in obj.properties)
                yield return new GMObjectPropertyInfo(property, obj);
        }
    }

    /// <summary>
    /// Returns highest override for given property and object
    /// </summary>
    public static GMOverriddenProperty? GetOverrideValue(GMObject _object, GMObjectProperty _property)
    {
        foreach (GMObject gmObject in EnumerateObjectHierarchy(_object))
        {
            var over = gmObject.overriddenProperties.Find(t => t.propertyId == _property);
            if (over != null)
                return over;
        }

        return null;
    }

    /// <summary>
    /// Returns default value for the property
    /// </summary>
    public static string GetDefaultValue(GMObject _object, GMObjectProperty _property)
    {
        var over = GetOverrideValue(_object, _property);
        return over?.value ?? _property.value;
    }

    private static readonly MethodInfo IsDirtyPropSetter = typeof(ResourceBase).GetProperty("isDirty",
        BindingFlags.Default | BindingFlags.Instance | BindingFlags.Public)!.SetMethod!;

    private static readonly object[] isDirtySetterParams = new object[] { false };

    public static void ResetDirty(ResourceBase _resource)
    {
        IsDirtyPropSetter.Invoke(_resource, isDirtySetterParams);
    }

    public static void ResetDirtyAll(GMProject _project)
    {
        ResetDirty( _project );
        foreach ( var resource in _project.resources.Select( t => t.id ) )
            ResetDirty( resource );
    }

    class PlaceholderLicensingModule : ILicenseModulesSource
    {
        public IEnumerable<string> Modules { get; } = new List<string>();
    }

}
