using Spectre.Console;
using System.Collections;
using YoYoStudio.Resources;

using static LDTK2GMS2Pipeline.LDTK.LDTKProject;
using IResource = LDTK2GMS2Pipeline.LDTK.LDTKProject.IResource;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public class ResourceCache
    {
        private readonly Dictionary<object, IResource> resourceById = new();
        private readonly Dictionary<ResourceKey, IMeta> metaByName = new();
        private readonly Dictionary<ResourceKey, IResource> resourceByName = new();

        public IReadOnlyDictionary<object, IResource> ResourcesById => resourceById;
        public IReadOnlyDictionary<ResourceKey, IMeta> MetaByName => metaByName;
        public IReadOnlyDictionary<ResourceKey, IResource> ResourceByName => resourceByName;

        public void ClearResources()
        {
            resourceById.Clear();
            resourceByName.Clear();
        }

        public void ClearMeta()
        {
            metaByName.Clear();
        }

        public bool RemoveResource( IResource _resource )
        {
            resourceById.Remove( _resource.uid );
            return resourceByName.Remove( new ResourceKey( _resource.identifier, _resource.GetType() ) );
        }

        public bool RemoveMeta( IMeta _meta )
        {
            return metaByName.Remove( new ResourceKey( _meta.identifier, _meta.GetType() ) );
        }

        public void AddResource( IResource _resource )
        {
            resourceById[_resource.uid] = _resource;
            resourceByName[new ResourceKey( _resource.identifier, _resource.GetType() )] = _resource;
        }

        public void AddMeta( IMeta _meta )
        {
            metaByName[new ResourceKey( _meta.identifier, _meta.GetType() )] = _meta;
        }

        public bool TryGetResource( object _uid, out IResource _result )
        {
            return resourceById.TryGetValue( _uid, out _result! );
        }

        public bool TryGetResource( ResourceKey _key, out IResource _result )
        {
            return resourceByName.TryGetValue( _key, out _result! );
        }

        public bool TryGetMeta( ResourceKey _key, out IMeta _result )
        {
            return metaByName.TryGetValue( _key, out _result! );
        }
    }

    public interface IResourceContainer
    {
        ResourceCache Cache { get; }

        public object GetNewUid( IResource _resource );

        public IEnumerable<Type> GetSupportedResources();

        public IList GetResourceList( Type _type );
        public IList GetMetaList( Type _type );

        public List<T> GetResourceList<T>()
            where T : IResource

        {
            return (List<T>) GetResourceList( typeof( T ) );
        }

        public List<T> GetMetaList<T>()
            where T : IMeta

        {
            return (List<T>) GetMetaList( typeof( T ) );
        }
    }
}

public static class IResourceContainerUtilities
{
    public enum LoggingLevel
    {
        Off,
        On,
        Auto,
    }

    public static LoggingLevel EnableLogging = LoggingLevel.Auto;

    private static bool LogsNeeded( Type _type )
    {
        switch ( EnableLogging )
        {
            case LoggingLevel.Off:
                return false;
            case LoggingLevel.On:
                return true;
            default:
                return _type != typeof( Field ) && _type != typeof( Level.Layer ) && _type != typeof( Level.EntityInstance ) && _type != typeof( Level.FieldInstance );
        }
    }

    public static LDTKProject TryGetProject( object _resource )
    {
        switch ( _resource )
        {
            case LDTKProject project:
                return project;
            case IResource res:
                return res.Project;
            default:
                throw new Exception( $"Object got no project references: {_resource.GetType()}" );
        }
    }

    /// <summary>
    /// Iterates though all resources
    /// </summary>
    public static IEnumerable<IResource> GetResources( this LDTKProject.IResourceContainer _container )
    {
        foreach ( Type type in _container.GetSupportedResources() )
            foreach ( IResource resource in _container.GetResourceList( type ) )
                yield return resource;
    }

    /// <summary>
    /// Iterates though all meta data dictionaries
    /// </summary>
    public static IEnumerable<(Type metaType, IList metaList)> GetMetas( this LDTKProject.IResourceContainer _container )
    {
        foreach ( var type in _container.GetSupportedResources() )
        {
            if ( typeof( IResource ).IsAssignableFrom( type ) )
            {
                var metaType = IResource.GetMetaType( type );
                yield return (metaType, _container.GetMetaList( metaType ));
            }
            else
                throw new Exception( $"Incorrect resource type: {type}" );
        }
    }

    /// <summary>
    /// Fills cache from list of existing resources
    /// </summary>
    public static void UpdateResourceCache( this LDTKProject.IResourceContainer _container )
    {
        var project = TryGetProject( _container );
        _container.Cache.ClearResources();
        foreach ( IResource resource in _container.GetResources() )
        {
            resource.Project = project;
            _container.Cache.AddResource( resource );

            if ( resource is LDTKProject.IResourceContainer container )
                container.UpdateResourceCache();
        }
    }

    /// <summary>
    /// Fills cache from list of existing meta data. Also assigns meta to resources matching by uids.
    /// If there are no resources found for given id - removes said meta data
    /// </summary>
    public static void UpdateMetaCache( this LDTKProject.IResourceContainer _container )
    {
        _container.Cache.ClearMeta();

        foreach ( var info in _container.GetMetas() )
        {
            foreach ( IMeta entry in info.metaList )
            {
                var meta = entry;

                if ( _container.Cache.TryGetResource( meta.uid, out var res ) )
                {
                    meta.Resource = res;
                    res.Meta = meta;
                    _container.Cache.AddMeta( meta );

                    if ( res is LDTKProject.IResourceContainer otherContainer )
                        otherContainer.UpdateMetaCache();
                }
                else
                {
                    _container.Cache.AddMeta( meta );
                }
            }

        }
    }

    /// <summary>
    /// Removes meta data that references no longer used keys
    /// </summary>
    public static void RemoveUnusedMeta<T>( this LDTKProject.IResourceContainer _container, IEnumerable<string> _neededKeys, Action<IMeta>? _onRemoved = null )
        where T : IMeta
    {
        var list = _container.GetMetaList<T>();
        var removedObjectNames = list.ExceptBy( _neededKeys, t => t.identifier, StringComparer.InvariantCultureIgnoreCase ).ToList();
        foreach ( var toRemove in removedObjectNames )
        {
            _onRemoved?.Invoke( toRemove );
            list.Remove( toRemove );
        }
    }

    /// <summary>
    /// Creates new resource of given type
    /// </summary>
    public static TResource Create<TResource>( this LDTKProject.IResourceContainer _container, string _name, object? _id = null )
        where TResource : IResource, new()
    {
        var key = new ResourceKey( _name, typeof( TResource ) );

        // If resource with fitting name exists - use it
        TResource result;
        bool existingResource;
        if ( _container.Cache.TryGetResource( key, out IResource? resource ) )
        {
            result = (TResource) resource;
            existingResource = true;
            if ( LogsNeeded( typeof( TResource ) ) )
                AnsiConsole.MarkupLineInterpolated( $"Found a new {typeof( TResource ).Name} [teal]{_name}[/]" );
        }
        else
        {
            result = new TResource
            {
                identifier = _name
            };
            result.Project = TryGetProject( _container );
            existingResource = false;
            var resourceList = _container.GetResourceList<TResource>();
            resourceList.Add( result );
            if ( LogsNeeded( typeof( TResource ) ) )
                AnsiConsole.MarkupLineInterpolated( $"Created a new {typeof( TResource ).Name} [teal]{_name}[/]" );
        }

        // If meta for given name already exists - use it
        if ( _container.Cache.TryGetMeta( key, out IMeta? meta ) )
        {
            if ( existingResource )
                throw new Exception( "Trying to create resource with already exists and got meta data" );

            result.Meta = meta;
            result.uid = meta.uid;
        }
        else
        {
            if ( !existingResource )
                result.uid = _id ?? _container.GetNewUid( result );
            result.Meta = result.CreateMeta( _name );

            IList metaList = _container.GetMetaList( result.MetaType );
            metaList.Add( result.Meta );
        }

        _container.Cache.AddResource( result );
        _container.Cache.AddMeta( result.Meta );

        return result;
    }

    /// <summary>
    /// Creates meta data for given resource, adds it to the lists
    /// </summary>
    public static void CreateMetaFor( this LDTKProject.IResourceContainer _container, IResource _resource, string _name )
    {
        if ( _resource.Meta != null )
            return;

        var key = new ResourceKey( _resource.identifier, _resource.GetType() );

        if ( _container.Cache.TryGetMeta( key, out _ ) )
            return;

        _resource.Meta = _resource.CreateMeta( _name );

        IList metaList = _container.GetMetaList( _resource.MetaType );
        metaList.Add( _resource.Meta );
        _container.Cache.AddMeta( _resource.Meta );
    }

    /// <summary>
    /// Creates abstract meta data, not assigned to any resource
    /// </summary>
    public static TMeta CreateMetaFor<TMeta>( this LDTKProject.IResourceContainer _container, string _name, object _uid )
        where TMeta : IMeta, new()
    {
        var key = new ResourceKey( _name, typeof( TMeta ) );

        if ( _container.Cache.TryGetMeta( key, out var existing ) )
            return (TMeta) existing;

        var meta = new TMeta
        {
            uid = _uid,
            identifier = _name
        };

        IList metaList = _container.GetMetaList( typeof( TMeta ) );
        metaList.Add( meta );
        _container.Cache.AddMeta( meta );

        return meta;
    }

    /// <summary>
    /// Returns resource with given name, or create it if missing.
    /// Resource MAY be null if it was removed manually on LDTK side later.
    /// </summary>
    public static bool CreateOrExisting<T>( this LDTKProject.IResourceContainer _container, string _name, out T? _resource, object? _id = null )
        where T : IResource, new()
    {
        var key = new ResourceKey( _name, typeof( T ) );
        if ( _container.Cache.TryGetMeta( key, out var meta ) )
        {
            _resource = (T) meta.Resource;
            if ( _resource is null && _container.Cache.TryGetResource( key, out var res ) )
            {
                _resource = (T) res;
                _resource.Meta = meta;
                meta.Resource = _resource;
                meta.uid = _resource.uid;
                if ( LogsNeeded( typeof( T ) ) )
                    AnsiConsole.MarkupLineInterpolated( $"Restored removed resource [teal]{_resource.identifier}[/] as {meta.identifier}" );
            }
            return false;
        }

        _resource = _container.Create<T>( _name, _id );
        return true;
    }

    /// <summary>
    /// Returns resource with given name, or create it if missing.
    /// If resource was removed in LDTK - will recreate it.
    /// </summary>
    public static bool CreateOrExistingForced<T>( this LDTKProject.IResourceContainer _container, string _name, out T _resource, object? _id = null )
        where T : IResource, new()
    {
        bool result = _container.CreateOrExisting( _name, out T? resourceTemp, _id );
        if ( resourceTemp != null )
        {
            _resource = resourceTemp;
            return result;
        }

        _resource = _container.Create<T>( _name, _id );
        return true;
    }

    public static bool Remove<TResource>( this LDTKProject.IResourceContainer _container, string _name )
        where TResource : IResource, new()
    {
        void Remove( IResource? _resource, IMeta? _meta )
        {
            if ( _meta != null )
            {
                var metaList = _container.GetMetaList( IResource.GetMetaType( typeof( TResource ) ) );
                var index = metaList.IndexOf( _meta );
                metaList.RemoveAt( index );
                _container.Cache.RemoveMeta( _meta );
            }

            if ( _resource != null )
            {
                var resList = _container.GetResourceList( typeof( TResource ) );
                var index = resList.IndexOf( _resource );
                resList.RemoveAt( index );
                _container.Cache.RemoveResource( _resource );
            }

            if ( LogsNeeded( typeof( TResource ) ) )
                AnsiConsole.MarkupLineInterpolated( $"Deleted {typeof( TResource ).Name} [teal]{_resource?.identifier ?? _meta?.identifier ?? "???"}[/]" );
        }

        var key = new ResourceKey( _name, typeof( TResource ) );

        if ( _container.Cache.TryGetMeta( key, out var meta ) )
        {
            Remove( meta.Resource, meta );
            return true;
        }

        if ( _container.Cache.TryGetResource( key, out var resource ) )
        {
            Remove( resource, resource.Meta );
            return true;
        }

        return false;
    }

    public static int RemoveDeletedItems<TMeta, TGMResource>( this LDTKProject.IResourceContainer _container, IList<TGMResource> _resource, Func<TGMResource, TMeta?, bool>? _canRemove = null, Func<TGMResource, string>? _getKey = null, Func<TMeta, bool>? _filterMeta = null )
        where TGMResource : ResourceBase
        where TMeta : IMeta
    {
        static string GetKeyDefault( TGMResource _res )
        {
            return _res.name;
        }

        if ( _getKey == null )
            _getKey = GetKeyDefault;

        IList<TMeta> metaList;
        try
        {
            metaList = _container.GetMetaList<TMeta>();
        }
        catch ( Exception )
        {
            metaList = Array.Empty<TMeta>();
        }

        var knownMeta = metaList.Where( t => _filterMeta == null || _filterMeta( t ) ).ToDictionary( t => t.identifier );

        int count = 0;
        for ( int i = _resource.Count - 1; i >= 0; i-- )
        {
            TGMResource res = _resource[i];
            string key = _getKey( res );

            bool knownByLDTK = knownMeta.TryGetValue( key, out TMeta? meta );
            // There may be meta without
            if ( knownByLDTK && meta!.Resource != null )
                continue;

            if ( _canRemove != null && !_canRemove( res, meta ) )
                continue;

            if ( meta != null )
                metaList.Remove( meta );
            _resource.RemoveAt( i );
            count++;
            AnsiConsole.MarkupLineInterpolated( $"Removed resource {key} of type [green]{typeof( TGMResource ).Name}[/]" );
        }

        return count;
    }

    public static T? GetMeta<T>( this LDTKProject.IResourceContainer _container, string _name )
        where T : IMeta
    {
        return _container.Cache.TryGetMeta( new ResourceKey( _name, typeof( T ) ), out var result ) ? (T) result : default;
    }

    public static T? GetResource<T>( this LDTKProject.IResourceContainer _container, string _name )
        where T : IResource
    {
        return _container.Cache.TryGetResource( new ResourceKey( _name, typeof( T ) ), out var result ) ? (T) result : default;
    }

    public static T? GetResource<T>( this LDTKProject.IResourceContainer _container, object _uid )
        where T : IResource
    {
        return _container.Cache.TryGetResource( _uid, out var result ) ? (T) result : default;
    }

    public static Dictionary<TKey, TValue> CreateResourceMap<TKey, TValue>( this LDTKProject.IResourceContainer _container, GMProject _gmProject )
        where TKey : ResourceBase
        where TValue : IResource
    {
        Dictionary<TKey, TValue> result = new();

        foreach ( var obj in _gmProject.GetResourcesByType<TKey>().Cast<TKey>() )
        {
            var res = _container.GetResource<TValue>( obj.name );
            if ( res != null )
                result.Add( obj, res );
        }

        return result;
    }

    public static Dictionary<string, TValue> CreateResourceMap<TValue>( this LDTKProject.IResourceContainer _container )
        where TValue : IResource
    {
        return _container.Cache.ResourceByName.Where( t => t.Value is TValue ).ToDictionary( _key => _key.Key.Name, _value => (TValue) _value.Value );
    }
}