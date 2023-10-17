using ProjectManager.GameMaker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProjectManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using YoYoStudio.Resources;
using System.Reflection;
using static ProjectManager.GameMaker.GMS2Project;
using LDTK2GMS2Pipeline.LDTK;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;

namespace LDTK2GMS2Pipeline;

public static class GMProjectUtilities
{
    public static Task<GMProject> LoadGMProject()
    {
        FileInfo? gmProjectFile = GMSUtilities.FindProjectFileInParent();
        if (gmProjectFile == null)
            throw new FileNotFoundException("GameMaker project file not found");

        FileIO.SetDefaultFileFunctions();
        MessageIO.SetDefaultMessageFunctions();
        ResourceInfo.FindAllResources();
        GMProject.LicenseModules = new PlaceholderLicensingModule();

        var loadingWait = new TaskCompletionSource<GMProject>();

        ProjectInfo.LoadProject( gmProjectFile.FullName, true, ( _r ) =>
        {
            Console.WriteLine( $"Success: {_r.name}" );
            loadingWait.TrySetResult( (GMProject)_r );
        }, ( _r, _progress ) =>
        {
            Console.WriteLine( $"Loading: {Math.Round( _progress * 100 )}%" );
        }, ( _r ) =>
        {
            Console.WriteLine( $"Failure: {_r.name}" );
            throw new Exception("Failed to load GameMaker project");
        } );

        return loadingWait.Task;
    }

    public static string GetFullPath( string _path, GMProject? _project = null )
    {
        return System.IO.Path.Combine( Path.GetDirectoryName( ProjectInfo.GetProjectPath( _project ?? ProjectInfo.Current ) ), _path );
    }

    /// <summary>
    /// Returns whole inheritance hierarchy for given object
    /// </summary>
    public static IEnumerable<GMObject> EnumerateObjectHierarchy( GMObject _object )
    {
        GMObject? obj = _object;
        while ( obj != null )
        {
            yield return obj;
            obj = obj.parentObjectId;
        }
    }

    public static bool IsInheritedFrom( GMObject _object, GMObject _parent )
    {
        GMObject? parentToCheck = _object.parentObjectId;
        while ( parentToCheck != null )
        {
            if ( parentToCheck == _parent )
                return true;
            parentToCheck = parentToCheck.parentObjectId;
        }

        return false;
    }

    public record GMObjectPropertyInfo( GMObjectProperty Property, GMObject DefinedIn );

    /// <summary>
    /// Returns all properties that given object has, including object they are defined in
    /// </summary>
    public static IEnumerable<GMObjectPropertyInfo> EnumerateAllProperties( GMObject _object )
    {
        foreach ( GMObject obj in GMProjectUtilities.EnumerateObjectHierarchy( _object ) )
        {
            foreach ( GMObjectProperty property in obj.properties )
                yield return new GMObjectPropertyInfo( property, obj );
        }
    }

    /// <summary>
    /// Returns highest override for given property and object
    /// </summary>
    public static GMOverriddenProperty? GetOverrideValue( GMObject _object, GMObjectProperty _property )
    {
        foreach ( GMObject gmObject in GMProjectUtilities.EnumerateObjectHierarchy( _object ) )
        {
            var over = gmObject.overriddenProperties.Find( t => t.propertyId == _property );
            if ( over != null )
                return over;
        }

        return null;
    }

    /// <summary>
    /// Returns default value for the property
    /// </summary>
    public static string GetDefaultValue( GMObject _object, GMObjectProperty _property )
    {
        var over = GetOverrideValue( _object, _property );
        return over?.value ?? _property.value;
    }

    class PlaceholderLicensingModule : ILicenseModulesSource
    {
        public IEnumerable<string> Modules { get; } = new List<string>();
    }

}
