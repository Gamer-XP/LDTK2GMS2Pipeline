using System.Collections;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public class Level : Resource<Level.MetaData>
    {
        public class MetaData : Meta<Level> { }

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
        public List<object> fieldInstances { get; set; } = new();
        public List<Layer> layerInstances { get; set; } = new();
        public List<Neighbour> __neighbours { get; set; } = new();

        public Level GetDatalessCopy( string _projectName )
        {
            Level result = (Level) this.MemberwiseClone();

            result.fieldInstances = new List<object>();
            result.layerInstances = new List<Layer>();
            result.externalRelPath = $"{Path.GetFileNameWithoutExtension( _projectName )}/{identifier}.ldtkl";

            return result;
        }

        public sealed class Layer
        {
            public string __identifier { get; set; }
            public string __type { get; set; }
            public int __cWid { get; set; }
            public int __cHei { get; set; }
            public int __gridSize { get; set; }
            public float __opacity { get; set; } = 1f;
            public int __pxTotalOffsetX { get; set; }
            public int __pxTotalOffsetY { get; set; }
            public int? __tilesetDefUid { get; set; }
            public string? __tilesetRelPath { get; set; }
            public string iid { get; set; }
            public int levelId { get; set; }
            public int layerDefUid { get; set; }
            public int pxOffsetX { get; set; }
            public int pxOffsetY { get; set; }
            public bool visible { get; set; }
            public List<object> optionalRules { get; set; } = new();
            public List<int> intGridCsv { get; set; } = new();
            public List<AutoLayerTile> autoLayerTiles { get; set; } = new();
            public int seed { get; set; }
            public int? overrideTilesetUid { get; set; }
            public List<TileInstance> gridTiles { get; set; } = new();
            public List<EntityInstance> entityInstances { get; set; } = new();

            public sealed class EntityInstance : Resource<EntityInstance.MetaData>
            {
                public class MetaData : LDTKProject.Meta<EntityInstance>
                {
                    
                }

                public string __identifier { get; set; }
                public List<int> __grid { get; set; }
                public List<double> __pivot { get; set; }
                public List<string> __tags { get; set; }
                public TileRect? __tile { get; set; }
                public string __smartColor { get; set; }
                public string iid { get; set; }
                public int __worldX { get; set; }
                public int __worldY { get; set; }
                public int width { get; set; }
                public int height { get; set; }
                public int defUid { get; set; }
                public List<int> px { get; set; } = new();
                public List<FieldInstance> fieldInstances { get; set; } = new();

                public sealed class FieldInstance
                {
                    public string __identifier { get; set; }
                    public string __type { get; set; }
                    public object __value { get; set; }
                    public object? __tile { get; set; }
                    public int defUid { get; set; }
                    public List<DefaultOverride> realEditorValues { get; set; } = new(0);

                    public FieldInstance(){}

                    public FieldInstance(Field _field)
                    {
                        __identifier = _field.identifier;
                        defUid = _field.uid;
                        __type = _field.__type;
                    }

                    public FieldInstance(Field _field, DefaultOverride.IdTypes _type, object _value) : this(_field)
                    {
                        SetValue(_type, _value);
                    }

                    public void SetValue( DefaultOverride.IdTypes _type, object _value  )
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
                }
            }

            public sealed class AutoLayerTile
            {
                public IList<int> px { get; set; }
                public IList<int> src { get; set; }
                public int f { get; set; }
                public int t { get; set; }
                public IList<int> d { get; set; }
                public int a { get; set; }
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

        public sealed class Neighbour
        {
            public string levelIid { get; set; }
            public string dir { get; set; }
        }
    }
}