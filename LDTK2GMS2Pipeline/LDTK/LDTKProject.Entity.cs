using System.Collections;
using Spectre.Console;
using System.Text.RegularExpressions;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.GMProjectUtilities;
using System.Reflection;
using LDTK2GMS2Pipeline.Sync;
using Microsoft.VisualBasic.FileIO;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public sealed class Entity : Resource<Entity.MetaData>, IResourceContainer
    {
        public sealed class MetaData : Meta<Entity>
        {
            public List<Field.MetaData> Properties { get; set; } = new();
        }

        public List<string> tags { get; set; } = new List<string>();
        public bool exportToToc { get; set; }
        public object doc { get; set; }
        public int width { get; set; } = 16;
        public int height { get; set; } = 16;
        public bool resizableX { get; set; } = false;
        public bool resizableY { get; set; } = false;
        public int? minWidth { get; set; }
        public int? maxWidth { get; set; }
        public int? minHeight { get; set; }
        public int? maxHeight { get; set; }
        public bool keepAspectRatio { get; set; }
        public float tileOpacity { get; set; } = 1f;
        public float fillOpacity { get; set; } = 0.08f;
        public float lineOpacity { get; set; } = 0f;
        public bool hollow { get; set; } = false;
        public string color { get; set; } = "#94D9B3";
        public string renderMode { get; set; } = "Tile";
        public bool showName { get; set; } = true;
        public int? tilesetId { get; set; }
        public string tileRenderMode { get; set; } = "FitInside";
        public TileRect? tileRect { get; set; }
        public IList<int> nineSliceBorders { get; set; } = Array.Empty<int>();
        public int maxCount { get; set; }
        public string limitScope { get; set; } = "PerLevel";
        public string limitBehavior { get; set; } = "MoveLastOne";
        public float pivotX { get; set; } = 0.5f;
        public float pivotY { get; set; } = 0.5f;
        public List<Field> fieldDefs { get; set; } = new List<Field>();

        public void Init( GMObject _object, Tileset _atlasTileset, SpriteAtlas _atlas )
        {
            SpriteAtlas.IAtlasItem? atlasItem = _atlas.Get( _object.spriteId?.name );

            if ( atlasItem == null )
                return;

            SpriteAtlas.AtlasRectangle rect = atlasItem.Rectangle;
            pivotX = rect.PivotX;
            pivotY = rect.PivotY;
            tilesetId = _atlasTileset.uid;

            width = _atlas.RoundToGrid( rect.Width - rect.EmptyLeft - rect.EmptyRight );
            height = _atlas.RoundToGrid( rect.Height - rect.EmptyTop - rect.EmptyBottom );

            tileRect = new TileRect()
            {
                tilesetUid = _atlasTileset.uid,
                x = rect.X,
                y = rect.Y,
                w = rect.Width,
                h = rect.Height
            };

            if ( atlasItem.Sprite.nineSlice?.enabled ?? false )
            {
                tileRect.x += rect.PaddingLeft;
                tileRect.y += rect.PaddingTop;
                tileRect.w -= rect.PaddingLeft + rect.PaddingRight;
                tileRect.h -= rect.PaddingTop + rect.PaddingBottom;
            }

            bool use9Slice = _object.spriteId?.nineSlice?.enabled ?? false;
            resizableX = resizableY = use9Slice;

            if ( !use9Slice )
                tileRenderMode = "FullSizeUncropped";
            else
            {
                tileRenderMode = "NineSlice";
                var sets = _object.spriteId.nineSlice;
                nineSliceBorders = new int[] { sets.top, sets.right, sets.bottom, sets.left };
            }
        }

        ResourceCache IResourceContainer.Cache { get; } = new();

        public object GetNewUid(IResource _resource )
        {
            return Project.GetNewUid( _resource );
        }

        public IEnumerable<Type> GetSupportedResources()
        {
            yield return typeof(Field);
        }

        public IList GetResourceList(Type _resourceType)
        {
            if (_resourceType == typeof(Field))
                return fieldDefs;
            throw new Exception($"Unknown type: {_resourceType}");
        }

        public IList GetMetaList(Type _metaType)
        {
            if (_metaType == typeof(Field.MetaData))
            {
                return Meta?.Properties ?? throw new Exception("Meta is null. Initialize it first.");
            }

            throw new Exception( $"Unknown type: {_metaType}" );
        }
    }

    public sealed class Field : Resource<Field.MetaData>
    {
        public class MetaData : Meta<Field>
        {
            public string? type { get; set; }
            public bool gotError { get; set; }

            public const string StringPropertyType = "$string";

            public string? GM2LDTK( string? _input )
            {
                return GM2LDTK( _input, type );
            }

            public string? LDTK2GM( object? _input )
            {
                return LDTK2GM( _input, type );
            }

            public static string? GM2LDTK( string? _input, string? _type )
            {
                if ( _input == null )
                    return null;

                switch ( _type )
                {
                    case null:
                    case StringPropertyType:
                        return _input;
                    default:
                        if ( !_input.StartsWith( _type ) )
                            return _input;
                        return _input.Substring( _type.Length + 1 );
                }
            }

            public static string? LDTK2GM( object? _input, string? _type )
            {
                if ( _input == null )
                    return null;

                switch ( _type )
                {
                    case null:
                    case StringPropertyType:
                        return _input.ToString();
                    default:
                        return $"{_type}.{_input.ToString()}";
                }
            }
        }

        public object doc { get; set; }
        public string __type { get; set; }
        public string type { get; set; }
        public bool isArray { get; set; } = false;
        public bool canBeNull { get; set; } = true;
        public object arrayMinLength { get; set; }
        public object arrayMaxLength { get; set; }
        public string editorDisplayMode { get; set; } = "RefLinkBetweenCenters";
        public int editorDisplayScale { get; set; } = 1;
        public string editorDisplayPos { get; set; } = "Above";
        public string editorLinkStyle { get; set; } = "CurvedArrow";
        public bool editorAlwaysShow { get; set; }
        public bool editorShowInWorld { get; set; } = false;
        public bool editorCutLongValues { get; set; } = true;
        public object editorTextSuffix { get; set; }
        public object editorTextPrefix { get; set; }
        public bool useForSmartColor { get; set; }
        public object min { get; set; }
        public object max { get; set; }
        public object regex { get; set; }
        public object acceptFileTypes { get; set; }
        public DefaultOverride? defaultOverride { get; set; }
        public object textLanguageMode { get; set; }
        public bool symmetricalRef { get; set; }
        public bool autoChainRef { get; set; } = true;
        public bool allowOutOfLevelRef { get; set; }
        public string allowedRefs { get; set; } = "OnlySame";
        public object allowedRefsEntityUid { get; set; }
        public List<string> allowedRefTags { get; set; } = new List<string>();
        public object tilesetUid { get; set; }
    }



    /// <summary>
    /// Returns all properties that given object has, including object they are defined in
    /// </summary>
    public static IEnumerable<GMObjectPropertyInfo> EnumerateAllProperties( GMObject _object )
    {
        yield return new GMObjectPropertyInfo( SharedData.FlipProperty, _object );

        foreach ( GMObjectPropertyInfo info in GMProjectUtilities.EnumerateAllProperties( _object ) )
        {
            yield return info;
        }
    }

    private static HashSet<GMObjectPropertyInfo> loggedErrorsFor = new();

    public void UpdateEntity( Entity _entity, GMObject _object )
    {
        var flipEnum = SharedData.GetFlipEnum( this );

        List<GMObjectPropertyInfo> definedProperties = EnumerateAllProperties( _object ).ToList();

        RemoveMissingProperties( definedProperties.Select( t => t.Property ) );

        foreach ( var propertyInfo in definedProperties )
        {
            UpdateField( propertyInfo );
        }

        void RemoveMissingProperties( IEnumerable<GMObjectProperty> _properties )
        {
            _entity.RemoveUnusedMeta<Field.MetaData>( _properties.Select( t => t.varName), _s =>
            {
                _entity.Remove<Field>(_s.identifier);
                AnsiConsole.MarkupLineInterpolated( $"[blue]Property [underline]{_s.identifier}[/] in object [teal]{_object.name}[/] is missing. Removing...[/]" );
            } );
        }

        void UpdateField( GMObjectPropertyInfo _info )
        {
            if ( _info.Property.multiselect )
            {
                AnsiConsole.MarkupLineInterpolated( $"[yellow]Multi-Select fields are not supported by LDTK yet. [green]{_info.Property.varName}[/] in [teal]{_object.name}[/] will be ignored.[/]" );
                return;
            }

            string? type = null;

            var fieldType = GM2LDTKField( _info.Property );

            if ( _info.Property.varType == eObjectPropertyType.List )
            {
                InitializeListProperty( _info, out LDTKProject.Enum en, out type );
                en.values.ForEach( t => t.id = Field.MetaData.GM2LDTK( t.id, type ) );

                fieldType.__type = string.Format( fieldType.__type, en.identifier );
                fieldType.type = string.Format( fieldType.type, en.uid );
            }
            else if ( _info.Property.varType == eObjectPropertyType.String )
            {
                type = Field.MetaData.StringPropertyType;
            }

            string defaultValue = GetDefaultValue( _object, _info.Property );

            bool convertSuccess = ConvertDefaultValue( _info.Property, defaultValue, out DefaultOverride defaultValueJson, type );

            if ( !convertSuccess && !loggedErrorsFor.Contains( _info ) )
            {
                AnsiConsole.MarkupLineInterpolated( $"[yellow]Error processing value '{defaultValue}' for field [green]{_info.Property.varName} [[{_info.Property.varType}]][/] in [teal]{_object.name}[/].[/]" );
                loggedErrorsFor.Add( _info );
            }

            bool justCreated = _entity.CreateOrExisting<Field>(_info.Property.varName, out var field);
            if ( field == null)
                return;

            if ( justCreated )
            {
                field.type = fieldType.type;
                field.__type = fieldType.__type;
                field.canBeNull = false;
                field.editorShowInWorld = true;
                field.editorDisplayMode = "NameAndValue";
                field.editorDisplayPos = "Beneath";
                field.defaultOverride = defaultValueJson;
                field.isArray = _info.Property.multiselect;

                if ( _info.Property.rangeEnabled )
                {
                    field.min = _info.Property.rangeMin;
                    field.max = _info.Property.rangeMax;
                }
            }
            else 
            if ( !defaultValueJson.Equals( field.defaultOverride ) )
            {
                if ( !field.Meta.gotError )
                    AnsiConsole.MarkupLineInterpolated( $"Default Value changed for field [green]{field.identifier}[/] in [teal]{_entity.identifier}[/] from '{field.defaultOverride?.ToString()}' to '{defaultValueJson?.ToString()}'" );
                field.defaultOverride = defaultValueJson;
            }

            field.Meta.type = type;
            field.Meta.gotError = !convertSuccess;
        }

        void InitializeListProperty( GMObjectPropertyInfo _info, out LDTKProject.Enum _enum, out string? _valueType )
        {
            if ( _info.Property.varName == SharedData.FlipStateEnumName )
            {
                _enum = flipEnum;
                _valueType = null;
                return;
            }

            string enumName = ProduceEnumName( _info.DefinedIn, _info.Property );

            this.CreateOrExistingForced( enumName, out _enum );

            _valueType = _enum.GetType( _info.Property );
        }

        static string ProduceEnumName( GMObject _object, GMObjectProperty _property )
        {
            return $"{_object.name}_{_property.varName}";
        }

        static (string type, string __type) GM2LDTKField( GMObjectProperty _property )
        {
            return _property.varType switch
            {
                eObjectPropertyType.Integer => ("F_Int", "Int"),
                eObjectPropertyType.Real => ("F_Float", "Float"),
                eObjectPropertyType.Resource => ("F_String", "String"),
                eObjectPropertyType.Color => ("F_Color", "Color"),
                eObjectPropertyType.Boolean => ("F_Bool", "Bool"),
                eObjectPropertyType.Expression => ("F_String", "String"),
                eObjectPropertyType.String => ("F_String", "String"),
                eObjectPropertyType.List => ("F_Enum({0})", "LocalEnum.{0}"),
                _ => throw new ArgumentOutOfRangeException()
            };
        }
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

    public static bool ConvertDefaultValue( GMObjectProperty _property, string _value, out DefaultOverride _result, string? _type = null )
    {
        try
        {
            if ( _type != null )
                _value = Field.MetaData.GM2LDTK( _value, _type );

            switch ( _property.varType )
            {
                case eObjectPropertyType.Boolean:
                    bool boolValue = _value switch
                    {
                        "0" => false,
                        "1" => true,
                        _ => bool.Parse( _value )
                    };
                    _result = new DefaultOverride( DefaultOverride.IdTypes.V_Bool, boolValue );
                    return true;
                case eObjectPropertyType.Integer:
                    _result = new DefaultOverride( DefaultOverride.IdTypes.V_Int, int.Parse( _value ) );
                    return true;
                case eObjectPropertyType.Real:
                    _result = new DefaultOverride( DefaultOverride.IdTypes.V_Float, float.Parse( _value ) );
                    return true;
                default:
                    _result = new DefaultOverride( DefaultOverride.IdTypes.V_String, _value );
                    return true;
            }
        }
        catch ( Exception )
        {
            _result = new DefaultOverride( DefaultOverride.IdTypes.V_String, _value );
            return false;
        }
    }
}