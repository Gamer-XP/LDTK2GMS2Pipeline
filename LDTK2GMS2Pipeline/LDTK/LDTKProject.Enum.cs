using System.Text.RegularExpressions;
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

        public class Value
        {
            public string? id { get; set; } = string.Empty;
            public TileRect? tileRect { get; set; }
            public int color { get; set; } = 0xFFFFFF;
        }

        public void ValidateValues( GMObjectProperty _property )
        {
            if ( !needValidation )
                return;

            needValidation = false;

            UpdateValues( _property.listItems );
        }

        public void UpdateValues( IEnumerable<string> _values )
        {
            Dictionary<string, Value> originals = values.ToDictionary( t => t.id! );
            values = _values.Select( _t => new Value() { id = _t } ).ToList();

            foreach ( Value value in values )
            {
                if ( value.id == null )
                    continue;

                int dotPosition = value.id.IndexOf( '.' );
                if ( dotPosition >= 0 )
                    value.id = value.id.Remove(0, dotPosition + 1);

                value.id = ToValidEnumValue(value.id);
            }

            foreach ( Value value in values )
            {
                if ( originals.TryGetValue( value.id!, out var previous ) )
                {
                    value.color = previous.color;
                    value.tileRect = previous.tileRect;
                }
            }
        }

        static string ToValidEnumValue( string _input )
        {
            Regex invalidCharsRgx = new Regex( "[^_a-zA-Z0-9]" );
            var result = invalidCharsRgx.Replace(_input, string.Empty);

            return result;
        }

        public static string ToPascalCase( string _input )
        {
            Regex invalidCharsRgx = new Regex( "[^_a-zA-Z0-9]" );
            Regex whiteSpace = new Regex( @"(?<=\s)" );
            Regex startsWithLowerCaseChar = new Regex( "^[a-z]" );
            Regex firstCharFollowedByUpperCasesOnly = new Regex( "(?<=[A-Z])[A-Z0-9]+$" );
            Regex lowerCaseNextToNumber = new Regex( "(?<=[0-9])[a-z]" );
            Regex upperCaseInside = new Regex( "(?<=[A-Z])[A-Z]+?((?=[A-Z][a-z])|(?=[0-9]))" );

            // replace white spaces with undescore, then replace all invalid chars with empty string
            var pascalCase = invalidCharsRgx.Replace( whiteSpace.Replace( _input, "_" ), string.Empty )
                // split by underscores
                .Split( new char[] { '_' }, StringSplitOptions.RemoveEmptyEntries )
                // set first letter to uppercase
                .Select( w => startsWithLowerCaseChar.Replace( w, m => m.Value.ToUpper() ) )
                // replace second and all following upper case letters to lower if there is no next lower (ABC -> Abc)
                .Select( w => firstCharFollowedByUpperCasesOnly.Replace( w, m => m.Value.ToLower() ) )
                // set upper case the first lower case following a number (Ab9cd -> Ab9Cd)
                .Select( w => lowerCaseNextToNumber.Replace( w, m => m.Value.ToUpper() ) )
                // lower second and next upper case letters except the last if it follows by any lower (ABcDEf -> AbcDef)
                .Select( w => upperCaseInside.Replace( w, m => m.Value.ToLower() ) );

            return string.Concat( pascalCase );
        }
    }
}