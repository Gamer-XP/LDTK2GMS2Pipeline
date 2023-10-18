using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public class Enum : Resource<Enum.MetaData>
    {
        public class MetaData : Meta<Enum> { }

        public List<Value> values { get; set; } = new();
        public object iconTilesetUid { get; set; }
        public string? externalRelPath { get; set; } = null;
        public object? externalFileChecksum { get; set; }
        public List<string> tags { get; set; } = new List<string>();

        /// <summary>
        /// Validation that shows if current enum needs validation
        /// </summary>
        private bool needValidation = true;

        /// <summary>
        /// Non-serialized value for type of current enum. Also serves as a flag if enum was validated or not.
        /// </summary>
        private string? type = null;

        public class Value
        {
            public string? id { get; set; } = string.Empty;
            public TileRect? tileRect { get; set; }
            public int color { get; set; } = 0xFFFFFF;
        }

        public string? GetType( GMObjectProperty _property )
        {
            if ( !needValidation )
                return type;

            type = null;
            needValidation = false;
            values = _property.listItems.Select( _t => new Value() { id = _t } ).ToList();
            bool isEnum = values.Any( t => t.id.Contains( '.' ) );
            if ( isEnum )
            {
                foreach ( Value value in values )
                {
                    int dotPosition = value.id.IndexOf( '.' );
                    if ( dotPosition <= 0 )
                        continue;

                    type = value.id.Substring( 0, dotPosition );
                    break;
                }
            }
            else
            {
                bool isString = values.All( t => t.id.StartsWith( '"' ) && t.id.EndsWith( '"' ) );
                if ( isString )
                    type = Field.MetaData.StringPropertyType;
            }

            return type;
        }
    }
}