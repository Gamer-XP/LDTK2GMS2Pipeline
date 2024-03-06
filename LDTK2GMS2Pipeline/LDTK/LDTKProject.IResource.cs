using System.Text.Json.Serialization;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public interface IMeta
    {
        public IResource? Resource { get; set; }

        public string identifier { get; set; }
        public object uid { get; set; }

        public static Type GetResourceType( Type _metaType )
        {
            foreach ( Type intType in _metaType.GetInterfaces() )
            {
                if ( intType.IsGenericType && intType.GetGenericTypeDefinition() == typeof( IMeta<,> ) )
                    return intType.GetGenericArguments()[0];
            }

            throw new Exception( $"Unable to get meta type from type {_metaType}" );
        }
    }

    public interface IMeta<TResource, TId> : IMeta
        where TResource : IResource
    {
        public new TResource? Resource { get; set; }
        public new TId uid { get; set; }

        object IMeta.uid
        {
            get => uid;
            set => uid = (TId) value;
        }

        IResource? IMeta.Resource
        {
            get => Resource;
            set => Resource = value is TResource cast? cast : throw new InvalidCastException($"Unable to cast {value} to {typeof(TResource)} for {identifier}");
        }
    }

    public abstract class Meta<TResource> : IMeta<TResource, int>
        where TResource : IResource
    {
        [JsonIgnore]
        public TResource? Resource { get; set; }

        public int uid { get; set; }
        public string identifier { get; set; }
    }

    public abstract class GuidMeta<TResource> : IMeta<TResource, string>
        where TResource : IResource
    {
        [JsonIgnore]
        public TResource? Resource { get; set; }

        public string uid { get; set; }
        public string identifier { get; set; }
    }

    public interface IResource
    {
        public string identifier { get; set; }
        public object uid { get; set; }

        public LDTKProject Project { get; set; }

        public IMeta? Meta { get; set; }
        public IMeta CreateMeta( string _name );
        public Type MetaType { get; }

        public static Type GetMetaType( Type _resourceType )
        {
            foreach ( Type intType in _resourceType.GetInterfaces() )
            {
                if ( intType.IsGenericType && intType.GetGenericTypeDefinition() == typeof( IResource<,> ) )
                    return intType.GetGenericArguments()[0];
            }

            throw new Exception( $"Unable to get meta type from type {_resourceType}" );
        }
    }

    public interface IResource<TMeta, TId> : IResource
        where TMeta : IMeta, new()
    {
        public new TMeta? Meta { get; set; }
        public new TId uid { get; set; }

        IMeta? IResource.Meta
        {
            get => Meta;
            set => Meta = (TMeta?) value;
        }

        object IResource.uid
        {
            get => uid;
            set => uid = (TId) value;
        }

        IMeta IResource.CreateMeta( string _name  )
        {
            var result = new TMeta
            {
                uid = uid,
                identifier = _name,
                Resource = this
            };
            return result;
        }

        Type IResource.MetaType => typeof( TMeta );
    }

    public class Resource<T> : IResource<T, int>
        where T : IMeta, new()
    {
        [JsonIgnore]
        public T? Meta { get; set; }

        [JsonIgnore]
        public LDTKProject Project { get; set; }

        public string identifier { get; set; }
        public int uid { get; set; }

        public virtual T CreateMeta( string _name )
        {
            return (T) ((IResource) this).CreateMeta( _name );
        }
    }

    public class GuidResource<T> : IResource<T, string>
        where T : IMeta, new()
    {
        [JsonIgnore]
        public T? Meta { get; set; }

        [JsonIgnore]
        public LDTKProject Project { get; set; }

        public string __identifier { get; set; }
        public string iid { get; set; }

        string IResource.identifier
        {
            get => __identifier;
            set => __identifier = value;
        }

        string IResource<T, string>.uid
        {
            get => iid;
            set => iid = value;
        }

        public virtual T CreateMeta( string _name )
        {
            return (T) ((IResource) this).CreateMeta( _name );
        }
    }

    public readonly struct ResourceKey
    {
        public readonly Type ResourceType;
        public readonly string Name;

        public ResourceKey( string _name, Type _resourceType )
        {
            Name = _name.ToLower();

            if ( typeof( IResource ).IsAssignableFrom( _resourceType ) )
                ResourceType = _resourceType;
            else if ( typeof( IMeta ).IsAssignableFrom( _resourceType ) )
                ResourceType = IMeta.GetResourceType( _resourceType );
            else
                throw new ArgumentException( $"Unable to get resource type from type {_resourceType}" );
        }

        public override int GetHashCode()
        {
            var has = new HashCode();
            has.Add( ResourceType );
            has.Add( Name );
            return has.ToHashCode();
        }
    }
}