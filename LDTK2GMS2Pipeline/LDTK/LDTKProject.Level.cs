using Spectre.Console;
using System.Collections;
using System.Text.Json.Serialization;
using LDTK2GMS2Pipeline.Utilities;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public class Level : Resource<Level.MetaData>, IResourceContainer
    {
        public class MetaData : Meta<Level>
        {
            public List<Layer.MetaData> Layers { get; set; } = new();
        }

        public string iid { get; set; }
        public int worldX { get; set; }
        public int worldY { get; set; }
        public int worldDepth { get; set; }
        public int pxWid { get; set; }
        public int pxHei { get; set; }
        public string __bgColor { get; set; }
        public object bgColor { get; set; }
        public bool useAutoIdentifier { get; set; }
        public object bgRelPath { get; set; }
        public object bgPos { get; set; }
        public double bgPivotX { get; set; }
        public double bgPivotY { get; set; }
        public string __smartColor { get; set; }
        public object __bgPos { get; set; }
        public string? externalRelPath { get; set; }
        public List<FieldInstance> fieldInstances { get; set; } = new();
        public List<Layer> layerInstances { get; set; } = new();
        public List<Neighbour> __neighbours { get; set; } = new();

        ResourceCache IResourceContainer.Cache { get; } = new();

        public object GetNewUid( IResource _resource )
        {
            return Guid.NewGuid().ToString();
        }

        public IEnumerable<Type> GetSupportedResources()
        {
            yield return typeof(Layer);
        }

        public IList GetResourceList( Type _type )
        {
            return _type == typeof(Layer) ? layerInstances : throw new Exception($"Unsupported type: {_type}");
        }

        public IList GetMetaList( Type _type )
        {
            return _type == typeof( Layer.MetaData ) ? Meta?.Layers ?? throw new Exception( "Meta is null. Initialize it first." ) : throw new Exception( $"Unsupported type: {_type}" );
        }

        public Level GetDatalessCopy( string _projectName )
        {
            Level result = (Level) this.MemberwiseClone();

            result.layerInstances = new ();
            result.externalRelPath = $"{Path.GetFileNameWithoutExtension( _projectName )}/{identifier}.ldtkl";

            return result;
        }

        /// <summary>
        /// Returns field instance for setting depth of the layer.
        /// Returned base on naming convention.
        /// </summary>
        public FieldInstance? GetLayerDepthField( Layer _layer )
        {
            string variableName = $"DEPTH_{_layer.__identifier}";
            var result = fieldInstances.Find( t => t.__identifier.Equals( variableName, StringComparison.InvariantCultureIgnoreCase ) );
            if (result == null)
                return null;

            if ( result.__type != "Int" )
            {
                Log.Write( $"[{Log.ColorError}]Level Field [{Log.ColorField}]{result.__identifier}[/] is supposed to be INT field.[/]" );
                return null;
            }

            return result;
        }

        public sealed class Layer : GuidResource<Layer.MetaData>, IResourceContainer
        {
            public class MetaData : GuidMeta<Layer>
            {
                public List<EntityInstance.MetaData> Entities { get; set; }= new();
            }

            public string __type { get; set; } = LayerTypes.Tiles;
            public int __cWid { get; set; }
            public int __cHei { get; set; }
            public int __gridSize { get; set; }
            public float __opacity { get; set; } = 1f;
            public int __pxTotalOffsetX { get; set; }
            public int __pxTotalOffsetY { get; set; }
            public int? __tilesetDefUid { get; set; }
            public string? __tilesetRelPath { get; set; }
            public int levelId { get; set; }
            public int layerDefUid { get; set; }
            public int pxOffsetX { get; set; }
            public int pxOffsetY { get; set; }
            public bool visible { get; set; }
            public List<object> optionalRules { get; set; } = new();
            public List<int> intGridCsv { get; set; } = new();
            public List<TileInstance> autoLayerTiles { get; set; } = new();
            public int seed { get; set; }
            public int? overrideTilesetUid { get; set; }
            public List<TileInstance> gridTiles { get; set; } = new();
            public List<EntityInstance> entityInstances { get; set; } = new();

            public IEnumerable<TileInstance> EnumerateAllTiles()
            {
                foreach (var tile in gridTiles)
                    yield return tile;

                foreach( var tile in autoLayerTiles)
                    yield return tile;
            }

            ResourceCache IResourceContainer.Cache { get; } = new();

            public object GetNewUid( IResource _resource )
            {
                return Guid.NewGuid().ToString();
            }

            public IEnumerable<Type> GetSupportedResources()
            {
                yield return typeof( EntityInstance );
            }

            public IList GetResourceList( Type _type )
            {
                return _type == typeof( EntityInstance ) ? entityInstances : throw new Exception( $"Unsupported type: {_type}" );
            }

            public IList GetMetaList( Type _type )
            {
                return _type == typeof( EntityInstance.MetaData ) ? Meta?.Entities ?? throw new Exception( "Meta is null. Initialize it first." ) : throw new Exception( $"Unsupported type: {_type}" );
            }

            public sealed class TileInstance
            {
                public IList<int> px { get; set; }
                public IList<int> src { get; set; }
                public int f { get; set; }
                public int t { get; set; }
                public IList<int> d { get; set; } = new List<int>();
                public float a { get; set; } = 1f;
            }
        }

        public sealed class EntityInstance : GuidResource<EntityInstance.MetaData>, IResourceContainer
        {
            public class MetaData : GuidMeta<EntityInstance>
            {
                public List<FieldInstance.MetaData> Fields { get; set; } = new();
            }

            public List<int> __grid { get; set; }
            public List<double> __pivot { get; set; }
            public List<string> __tags { get; set; }
            public TileRect? __tile { get; set; }
            public string __smartColor { get; set; }
            public int __worldX { get; set; }
            public int __worldY { get; set; }
            public int width { get; set; }
            public int height { get; set; }
            public int defUid { get; set; }
            public List<int> px { get; set; } = new();
            public List<FieldInstance> fieldInstances { get; set; } = new();

            ResourceCache IResourceContainer.Cache { get; } = new();

            public object GetNewUid( IResource _resource )
            {
                return _resource is FieldInstance inst? inst.defUid : throw new Exception( $"Unsupported resource: {_resource}" );
            }

            public override MetaData CreateMeta(string _name)
            {
                return base.CreateMeta(_name);
            }

            public IEnumerable<Type> GetSupportedResources()
            {
                yield return typeof( FieldInstance );
            }

            public IList GetResourceList( Type _type )
            {
                return _type == typeof( FieldInstance ) ? fieldInstances : throw new Exception( $"Unsupported type: {_type}" );
            }

            public IList GetMetaList( Type _type )
            {
                return _type == typeof( FieldInstance.MetaData ) ? Meta?.Fields ?? throw new Exception( "Meta is null. Initialize it first." ) : throw new Exception( $"Unsupported type: {_type}" );
            }
        }

        public sealed class FieldInstance : IResource<FieldInstance.MetaData, int>
        {
            public class MetaData : IMeta<FieldInstance, int>
            {
                [JsonIgnore]
                public FieldInstance? Resource { get; set; }

                public int uid { get; set; }
                public string identifier { get; set; }

                public bool GotError { get; set; } = false;
            }

            [JsonIgnore]
            public LDTKProject Project { get; set; }

            [JsonIgnore]
            public MetaData? Meta { get; set; }

            [JsonIgnore]
            public bool IsOverridden => realEditorValues.Count > 0 && realEditorValues[0] != null;

            public string __identifier { get; set; }
            public string __type { get; set; }
            public object __value { get; set; }
            public object? __tile { get; set; }
            public int defUid { get; set; }
            public List<DefaultOverride> realEditorValues { get; set; } = new( 0 );

            string IResource.identifier
            {
                get => __identifier;
                set => __identifier = value;
            }

            int IResource<FieldInstance.MetaData, int>.uid
            {
                get => defUid;
                set => defUid = value;
            }

            public void SetValue( DefaultOverride.IdTypes _type, object _value )
            {
                __value = _value;
                realEditorValues = new()
                {
                    new DefaultOverride(_type, _value)
                };
            }

            public void SetValue( DefaultOverride _value )
            {
                __value = _value.values?[0];
                realEditorValues = new List<DefaultOverride>() { _value };
            }

            public void SetValues( DefaultOverride[]? _values )
            {
                if (_values == null || _values.Length == 0)
                {
                    __value = null;
                    realEditorValues = new List<DefaultOverride>();
                    return;
                }

                if (_values.Length == 1)
                {
                    SetValue(_values[0]);
                    return;
                }

                __value = _values.Select(t => t.values[0]).ToArray();
                realEditorValues = _values.ToList();
            }
        }

        public sealed class Neighbour
        {
            public string levelIid { get; set; }
            public string dir { get; set; }
        }
    }
}