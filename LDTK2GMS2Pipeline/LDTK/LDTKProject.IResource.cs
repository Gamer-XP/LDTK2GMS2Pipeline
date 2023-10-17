using System.Collections;
using Spectre.Console;
using System.Text.Json.Serialization;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public interface IMeta
    {
        public IResource Resource { get; set; }
        public int uid { get; set; }

        public static Type GetResourceType( Type _metaType )
        {
            foreach ( Type intType in _metaType.GetInterfaces() )
            {
                if ( intType.IsGenericType && intType.GetGenericTypeDefinition() == typeof( IMeta<> ) )
                    return intType.GetGenericArguments()[0];
            }

            throw new Exception( $"Unable to get meta type from type {_metaType}" );
        }
    }

    public interface IMeta<TResource> : IMeta
        where TResource : IResource
    {
        public new TResource Resource { get; set; }

        IResource IMeta.Resource
        {
            get => Resource;
            set => Resource = (TResource) value;
        }
    }

    public abstract class Meta<TResource> : IMeta<TResource>
        where TResource : IResource
    {
        [JsonIgnore]
        public TResource Resource { get; set; }

        public int uid { get; set; }
    }

    public interface IResource
    {
        public string identifier { get; set; }
        public int uid { get; set; }

        public IMeta Meta { get; set; }
        public IMeta CreateMeta();
        public Type MetaType { get; }

        public static Type GetMetaType( Type _resourceType )
        {
            foreach ( Type intType in _resourceType.GetInterfaces() )
            {
                if ( intType.IsGenericType && intType.GetGenericTypeDefinition() == typeof( IResource<> ) )
                    return intType.GetGenericArguments()[0];
            }

            throw new Exception($"Unable to get meta type from type {_resourceType}");
        }
    }

    public interface IResource<T> : IResource
        where T : IMeta, new()
    {
        public T Meta { get; set; }

        IMeta IResource.Meta
        {
            get => Meta;
            set => Meta = (T) value;
        }

        IMeta IResource.CreateMeta()
        {
            var result = new T
            {
                uid = uid,
                Resource = this
            };
            return result;
        }

        Type IResource.MetaType => typeof( T );
    }

    public class Resource<T> : IResource<T>
        where T : IMeta, new()
    {
        [JsonIgnore]
        public T Meta { get; set; }

        public string identifier { get; set; }
        public int uid { get; set; }
    }

    public readonly struct ResourceKey
    {
        public readonly Type ResourceType;
        public readonly string Name;

        public ResourceKey( string _name, Type _resourceType )
        {
            Name = _name.ToLower();
            
            if (typeof(IResource).IsAssignableFrom(_resourceType))
                ResourceType = _resourceType;
            else if (typeof(IMeta).IsAssignableFrom(_resourceType))
                ResourceType = IMeta.GetResourceType(_resourceType);
            else
                throw new ArgumentException($"Unable to get resource type from type {_resourceType}");
        }

        public override int GetHashCode()
        {
            var has = new HashCode();
            has.Add( ResourceType );
            has.Add( Name );
            return has.ToHashCode();
        }
    }

    public class ResourceCache
    {
        private readonly Dictionary<int, IResource> resourceById = new();
        private readonly Dictionary<ResourceKey, IMeta> metaByName = new();
        private readonly Dictionary<ResourceKey, IResource> resourceByName = new();

        public IReadOnlyDictionary<int, IResource> ResourcesById => resourceById;
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

        public void AddResource( IResource _resource )
        {
            resourceById[_resource.uid] = _resource;
            resourceByName[new ResourceKey(_resource.identifier, _resource.GetType())] = _resource;
        }

        public void AddMeta( string _name, IMeta _meta )
        {
            metaByName[new ResourceKey( _name, _meta.GetType() )] = _meta;
        }

        public bool TryGetResource(int _uid, out IResource _result )
        {
            return resourceById.TryGetValue(_uid, out _result!);
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
        public int GetNewUid();

        public IEnumerable<Type> GetSupportedResources();
        public IList GetResourceList( Type _resourceType );
        public IDictionary GetMetaDictionary( Type _metaType );
    }
}

public static class IResourceUtilities
{
    /// <summary>
    /// Iterates though all resources
    /// </summary>
    public static IEnumerable<IResource> GetResources( this LDTKProject.IResourceContainer _container )
    {
        foreach (Type type in _container.GetSupportedResources())
        foreach (IResource resource in _container.GetResourceList(type))
            yield return resource;
    }

    /// <summary>
    /// Iterates though all meta data dictionaries
    /// </summary>
    public static IEnumerable<(Type metaType, IDictionary metaDict)> GetMetas( this LDTKProject.IResourceContainer _container )
    {
        foreach (var type in _container.GetSupportedResources() )
        {
            if (typeof(IResource).IsAssignableFrom(type))
            {
                var metaType = IResource.GetMetaType(type);
                yield return (metaType, _container.GetMetaDictionary(metaType));
            }
            else
                throw new Exception($"Incorrect resource type: {type}");
        }
    }

    /// <summary>
    /// Fills cache from list of existing resources
    /// </summary>
    public static void UpdateResourceCache( this LDTKProject.IResourceContainer _container )
    {
        _container.Cache.ClearResources();
        foreach ( IResource resource in _container.GetResources() ) 
            _container.Cache.AddResource(resource);
    }

    /// <summary>
    /// Fills cache from list of existing meta data. Also assigns meta to resources matching by uids.
    /// If there are no resources found for given id - removes said meta data
    /// </summary>
    public static void UpdateMetaCache( this LDTKProject.IResourceContainer _container )
    {
        _container.Cache.ClearMeta();

        HashSet<string> missingIds = new HashSet<string>();
        foreach ( var info in _container.GetMetas() )
        {
            missingIds.Clear();
            foreach ( DictionaryEntry entry in info.metaDict )
            {
                var key = (string) entry.Key;
                var meta = (IMeta) entry.Value!;

                if ( _container.Cache.TryGetResource( meta.uid, out var res ) )
                {
                    meta.Resource = res;
                    res.Meta = meta;
                    _container.Cache.AddMeta( key, meta );
                }
                else
                {
                    missingIds.Add( key );
                }
            }

            foreach ( string id in missingIds )
            {
                AnsiConsole.MarkupLineInterpolated( $"[green]Resource with name {id} was removed.[/]" );
                info.metaDict.Remove( id );
            }
        }
    }

    /// <summary>
    /// Removes meta data that references no longer used keys
    /// </summary>
    public static void RemoveUnusedMeta<T>( this LDTKProject.IResourceContainer _container, IEnumerable<string> _neededKeys, Action<string>? _onRemoved = null )
        where T : IMeta
    {
        var dict = _container.GetMetaDictionary(typeof(T));
        var removedObjectNames = dict.Keys.Cast<string>().Except( _neededKeys, StringComparer.InvariantCultureIgnoreCase ).ToList();
        foreach ( string key in removedObjectNames )
        {
            dict.Remove( key );
            _onRemoved?.Invoke( key );
        }
    }

    /// <summary>
    /// Creates new resource of given type
    /// </summary>
    public static T Create<T>( this LDTKProject.IResourceContainer _container, string _name )
        where T : IResource, new()
    {
        var key = new ResourceKey( _name, typeof( T ) );

        var resourceList = _container.GetResourceList(typeof(T));

        // If resource with fitting name exists - use it
        T result;
        bool existingResource;
        if ( _container.Cache.TryGetResource( key, out IResource? resource ) )
        {
            result = (T) resource;
            existingResource = true;
            AnsiConsole.MarkupLineInterpolated( $"Found a new {typeof( T ).Name} [teal]{_name}[/]" );
        }
        else
        {
            result = new T
            {
                identifier = _name
            };
            existingResource = false;
            resourceList.Add( result );
            AnsiConsole.MarkupLineInterpolated( $"Created a new {typeof( T ).Name} [teal]{_name}[/]" );
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
                result.uid = _container.GetNewUid();
            result.Meta = result.CreateMeta();
        }

        _container.Cache.AddResource(result);
        _container.Cache.AddMeta(key.Name, result.Meta);

        _container.GetMetaDictionary( result.MetaType )[key.Name] = result.Meta;

        return result;
    }

    /// <summary>
    /// Returns resource with given name, or create it if missing.
    /// Resource MAY be null if it was removed manually on LDTK side later.
    /// </summary>
    public static bool CreateOrExisting<T>( this LDTKProject.IResourceContainer _container, string _name, out T? _resource )
        where T : IResource, new()
    {
        if ( _container.Cache.TryGetMeta( new ResourceKey( _name, typeof( T ) ), out var meta ) )
        {
            _resource = (T) meta.Resource;
            return false;
        }

        _resource = _container.Create<T>( _name );
        return true;
    }

    /// <summary>
    /// Returns resource with given name, or create it if missing.
    /// If resource was removed in LDTK - will recreate it.
    /// </summary>
    public static bool CrateOrExistingForced<T>( this LDTKProject.IResourceContainer _container, string _name, out T _resource )
        where T : IResource, new()
    {
        bool result = _container.CreateOrExisting( _name, out T? resourceTemp );
        if ( resourceTemp != null )
        {
            _resource = resourceTemp;
            return result;
        }

        _resource = _container.Create<T>( _name );
        return true;
    }

    public static T? GetMeta<T>( this LDTKProject.IResourceContainer _container, string _name )
        where T : IMeta
    {
        return _container.Cache.TryGetMeta( new ResourceKey( _name, typeof( T ) ), out var result )? (T) result : default;
    }

    public static T? GetResource<T>( this LDTKProject.IResourceContainer _container, string _name )
        where T : IResource
    {
        return _container.Cache.TryGetResource( new ResourceKey( _name, typeof( T ) ), out var result ) ? (T) result : default;
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