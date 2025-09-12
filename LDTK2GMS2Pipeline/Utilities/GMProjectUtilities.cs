using System.Diagnostics;
using System.Dynamic;
using System.Reflection;
using ImpromptuInterface;
using LDTK2GMS2Pipeline.Wrapper;
using Spectre.Console;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Utilities;

public static class GMProjectUtilities
{
    public static async Task<GMProject> LoadGMProject(FileInfo _file)
    {
        Debug.Assert(_file != null, "GameMaker project file not found");

        FileIO.SetDefaultFileFunctions();
        MessageIO.SetDefaultMessageFunctions();
        ResourceInfo.FindAllResources();
        GMProject.LicenseModules = new PlaceholderLicensingModule().ActLike<ILicenseModulesSource>();
        
        string GenerateProgressLine( float _progress )
        {
            int border = (int)(_progress * 10);
            return $"\rLoading: [{ string.Concat( Enumerable.Range(1, 10).Select( i => i <= border? '-' : ' ' ) ) }] { Math.Round(_progress * 100) }%";
        }

        GMProject result = await GMAssemblyUtilities.InvokeTaskWithResult<GMProject>(( _wrapper ) =>
        {
            return _wrapper.Load(_file, ( _progress ) =>
            {
                try
                {
                    var pos = Console.GetCursorPosition();
                    Console.Write(GenerateProgressLine(_progress));
                    Console.SetCursorPosition(0, pos.Top);
                }
                catch
                {
                    //ignored
                }
            });
        });
        
        Console.WriteLine();
        AnsiConsole.MarkupLineInterpolated($"Loaded project {result.name}");

        return result;
    }

    public static Task SaveGMProject(GMProject _project)
    {
        if (!_project.isDirty)
            return Task.CompletedTask;
        
        return GMAssemblyUtilities.InvokeTask(( _wrapper ) => _wrapper.Save(_project));
    }

    private static Dictionary<GMProject, string> _projectPathCache = new();
    
    public static void SetProjectPath( GMProject _project, string _path )
    {
        _projectPathCache[_project] = _path;
    }

    public static string GetProjectPath( this GMProject _project )
    {
        return _projectPathCache[_project];
    }

    public static string GetFullPath(string _path, GMProject? _project = null)
    {
        return Path.Combine(Path.GetDirectoryName(GetProjectPath(_project ?? ProjectInfo.Current)), _path);
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
    
    class PlaceholderLicensingModule : DynamicObject
    {
        public IEnumerable<string> Modules { get; } = new List<string>();
        public void AddMissingOptions() {}
    }
}
