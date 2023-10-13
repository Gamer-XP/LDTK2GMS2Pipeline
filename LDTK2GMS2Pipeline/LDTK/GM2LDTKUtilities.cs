using static LDTK2GMS2Pipeline.LDTK.LDTKProject;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject.Enum;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace LDTK2GMS2Pipeline.LDTK;

public static class GM2LDTKUtilities
{
    #region Entities

    public static Entity CreateEntity( LDTKProject _project, GMObject _object, Tileset _tileset, SpriteAtlas _atlas )
    {
        Entity entity = new()
        {
            identifier = _object.name,
            uid = _project.GetNewUid()
        };

        _project.defs.entities.Add( entity );

        SpriteAtlas.IAtlasItem? atlasItem = _atlas.Get( _object.spriteId?.name );

        if ( atlasItem == null )
            return entity;

        SpriteAtlas.AtlasRectangle rect = atlasItem.Rectangle;
        entity.pivotX = rect.PivotX;
        entity.pivotY = rect.PivotY;

        entity.tilesetId = _tileset.uid;

        entity.width = _atlas.RoundToGrid( rect.Width - rect.EmptyLeft - rect.EmptyRight );
        entity.height = _atlas.RoundToGrid( rect.Height - rect.EmptyTop - rect.EmptyBottom );

        entity.tileRect = new TileRect()
        {
            tilesetUid = _tileset.uid,
            x = rect.X,
            y = rect.Y,
            w = rect.Width,
            h = rect.Height
        };

        bool use9Slice = _object.spriteId?.nineSlice?.enabled ?? false;

        entity.resizableX = entity.resizableY = use9Slice;

        if (!use9Slice )
            entity.tileRenderMode = "FullSizeUncropped";
        else
        {
            entity.tileRenderMode = "NineSlice";
            var sets = _object.spriteId.nineSlice;
            entity.nineSliceBorders = new int[] { sets.top, sets.right, sets.bottom, sets.left };
        }

        return entity;
    }

    public record GMObjectPropertyInfo(GMObjectProperty Property, GMObject DefinedIn);

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

    private static readonly GMObjectProperty XScaleProperty = new ()
    {
        varType = eObjectPropertyType.Real,
        varName = "image_xscale",
        value = "1"
    };

    private static readonly GMObjectProperty YScaleProperty = new()
    {
        varType = eObjectPropertyType.Real,
        varName = "image_yscale",
        value = "1"
    };

    private static readonly GMObjectProperty AngleProperty = new()
    {
        varType = eObjectPropertyType.Real,
        varName = "image_angle",
        value = "0"
    };

    private static readonly GMObjectProperty BlendProperty = new()
    {
        varType = eObjectPropertyType.Color,
        varName = "image_blend",
        value = 0xFFFFFF.ToString()
    };

    /// <summary>
    /// Returns all properties that given object has, including object they are defined in
    /// </summary>
    public static IEnumerable<GMObjectPropertyInfo> EnumerateAllProperties( GMObject _object, MetaData.Options _options )
    {
        if ( _options.ImportImageXScale)
            yield return new GMObjectPropertyInfo( XScaleProperty, _object);
        if ( _options.ImportImageYScale )
            yield return new GMObjectPropertyInfo( YScaleProperty, _object );
        if ( _options.ImportImageAngle )
            yield return new GMObjectPropertyInfo( AngleProperty, _object );
        if ( _options.ImportImageBlend )
            yield return new GMObjectPropertyInfo( BlendProperty, _object );

        foreach ( GMObject obj in EnumerateObjectHierarchy( _object ) )
        {
            foreach ( GMObjectProperty property in obj.properties )
                yield return new GMObjectPropertyInfo(property, obj );
        }
    }

    /// <summary>
    /// Returns highest override for given property and object
    /// </summary>
    public static GMOverriddenProperty? GetOverrideValue( GMObject _object, GMObjectProperty _property )
    {
        foreach ( GMObject gmObject in EnumerateObjectHierarchy( _object ) )
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
        var over = GetOverrideValue(_object, _property);
        return over?.value ?? _property.value;
    }

    private static HashSet<GMObjectPropertyInfo> loggedErrorsFor = new();

    public static void UpdateEntity( LDTKProject _project, GMObject _object, Entity _entity, bool _skipAddEntityLogs )
    {
        List<GMObjectPropertyInfo> definedProperties = EnumerateAllProperties( _object, _project.Meta.options ).ToList();

        MetaData.ObjectInfo meta = _project.Meta.Get<MetaData.ObjectInfo>( _entity.identifier )!;

        RemoveMissingProperties( meta, definedProperties.Select( t => t.Property) );

        foreach ( var propertyInfo in definedProperties )
        {
            UpdateField( propertyInfo, meta.Properties.GetValueOrDefault(propertyInfo.Property.varName) );
        }

        void RemoveMissingProperties( MetaData.ObjectInfo _meta, IEnumerable<GMObjectProperty> _properties )
        {
            var missingProperties = _meta.Properties.ExceptBy( _properties.Select( t => t.varName ), t => t.Key );
            foreach ( var property in missingProperties )
            {
                AnsiConsole.MarkupLineInterpolated( $"[blue]Property [underline]{property.Key}[/] in object [teal]{_object.name}[/] is missing. Removing...[/]" );
                _entity.fieldDefs.RemoveAll( t => t.uid == property.Value.uid );
                _meta.Properties.Remove( property.Key );
            }
        }

        void UpdateField( GMObjectPropertyInfo _info, MetaData.ObjectInfo.PropertyInfo? _propertyMeta )
        {
            if (_info.Property.multiselect)
            {
                AnsiConsole.MarkupLineInterpolated($"[yellow]Multi-Select fields are not supported by LDTK yet. [green]{_info.Property.varName}[/] in [teal]{_object.name}[/] will be ignored.[/]");
                return;
            }

            string? type = null;

            string? ProcessValue( string? _input, bool _isEnumValue )
            {
                string? ApplyType()
                {
                    switch ( type )
                    {
                        case null:
                            return _input;
                        case "$string":
                            return _input.Substring( 1, _input.Length - 2 );
                        default:
                            if ( !_input.StartsWith( type ) )
                                return _input;
                            return _input.Substring( type.Length + 1 );
                    }
                }

                var result = ApplyType();
                if (_isEnumValue && result != null)
                    result = ToValueEnumValue(result);
                return result;
            }

            var fieldType = GM2LDTKField( _info.Property );
            bool forceUpper = false;

            if ( _info.Property.varType == eObjectPropertyType.List )
            {
                InitializeListProperty( _info, out LDTKProject.Enum en, out type );
                forceUpper = true;
                en.values.ForEach( t => t.id = ProcessValue( t.id, true ) );

                fieldType.__type = string.Format( fieldType.__type, en.identifier );
                fieldType.type = string.Format( fieldType.type, en.uid );
            }
            else if (_info.Property.varType == eObjectPropertyType.String)
            {
                type = "$string";
            }

            string defaultValue = GetDefaultValue( _object, _info.Property );

            bool convertSuccess = ConvertDefaultValue( _info.Property, ProcessValue( defaultValue, forceUpper ), out DefaultOverride defaultValueJson );

            if ( !convertSuccess && !loggedErrorsFor.Contains( _info ) )
            {
                AnsiConsole.MarkupLineInterpolated( $"[yellow]Error processing value '{defaultValue}' for field [green]{_info.Property.varName} [[{_info.Property.varType}]][/] in [teal]{_object.name}[/].[/]" );
                loggedErrorsFor.Add( _info );
            }

            Entity.FieldDef? field = null;
            if ( _propertyMeta != null)
                field = _entity.fieldDefs.Find( t => t.uid == _propertyMeta.uid);

            if (field == null)
            {
                field = new Entity.FieldDef()
                {
                    identifier = _info.Property.varName,
                    type = fieldType.type,
                    __type = fieldType.__type,
                    uid = _project.GetNewUid(),
                    canBeNull = false,
                    editorShowInWorld = true,
                    editorDisplayMode = "NameAndValue",
                    editorDisplayPos = "Beneath",
                    defaultOverride = defaultValueJson,
                    isArray = _info.Property.multiselect
                };

                if (_info.Property.rangeEnabled)
                {
                    field.min = _info.Property.rangeMin;
                    field.max = _info.Property.rangeMax;
                }

                _entity.fieldDefs.Add( field );

                if ( !_skipAddEntityLogs )
                    AnsiConsole.MarkupLineInterpolated( $"Property [green]{field.identifier} [[{field.__type}]][/] was added to the object [teal]{_object.name}[/]" );
            }
            else if ( !defaultValueJson.Equals( field.defaultOverride ) )
            {
                if ( _propertyMeta == null || !_propertyMeta.gotError )
                    AnsiConsole.MarkupLineInterpolated( $"Default Value changed for field [green]{field.identifier}[/] in [teal]{_entity.identifier}[/] from '{field.defaultOverride?.ToString()}' to '{defaultValueJson?.ToString()}'");
                field.defaultOverride = defaultValueJson;
            }

            if (_propertyMeta == null)
            {
                _propertyMeta = new MetaData.ObjectInfo.PropertyInfo() { uid = field.uid };
                meta.Properties.Add( _info.Property.varName, _propertyMeta );
            }

            _propertyMeta.type = type;
            _propertyMeta.gotError = !convertSuccess;
        }

        void InitializeListProperty( GMObjectPropertyInfo _info, out LDTKProject.Enum _enum, out string? _valueType )
        {
            _valueType = null;
            string enumName = ProduceEnumName( _info.DefinedIn, _info.Property );
            _enum = _project.defs.enums.Find( _t => _t.identifier == enumName );
            if ( _enum == null )
            {
                _enum = new LDTKProject.Enum
                {
                    identifier = enumName,
                    uid = _project.GetNewUid()
                };
                _project.defs.enums.Add( _enum );
            }

            _enum.values = _info.Property.listItems.Select( _t => new Value() { id = _t } ).ToList();
            bool isEnum = _enum.values.Any( t => t.id.Contains( '.' ) );
            if ( isEnum )
            {
                foreach ( Value value in _enum.values )
                {
                    int dotPosition = value.id.IndexOf( '.' );
                    if ( dotPosition <= 0 )
                        continue;

                    _valueType = value.id.Substring( 0, dotPosition );
                    break;
                }
            }
            else
            {
                bool isString = _enum.values.All( t => t.id.StartsWith( '"' ) && t.id.EndsWith( '"' ) );
                if ( isString )
                    _valueType = "$string";
            }
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

    #endregion

    public static string ToValueEnumValue( string _input )
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

    public static bool ConvertDefaultValue( GMObjectProperty _property, string _value, out DefaultOverride _result )
    {
        try
        {
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
