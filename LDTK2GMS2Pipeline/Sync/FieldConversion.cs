using LDTK2GMS2Pipeline.LDTK;
using Spectre.Console;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal class FieldConversion
{
    public const string StringPropertyType = "$string";

    public enum FieldType
    {
        Int,
        Float,
        String,
        Color,
        Bool,
        Enum,
        EntityRef,
        Multilines,
        FilePath,
        Tile,
        Point
    }
    
    public struct FieldTypeInfo
    {
        public FieldType enumType;
        public string __type;
        public string type;
        public bool isArray;

        public FieldTypeInfo( FieldType enumType, string __type, string type, bool isArray = false )
        {
            this.enumType = enumType;
            this.__type = __type;
            this.type = type;
            this.isArray = isArray;
        }
    }
    
    private static readonly FieldTypeInfo[] FieldTypeInfoList = new[]
    {
        new FieldTypeInfo( FieldType.Int,"Int", "F_Int"),
        new FieldTypeInfo(FieldType.Float,"Float", "F_Float"),
        new FieldTypeInfo(FieldType.String,"String", "F_String"),
        new FieldTypeInfo(FieldType.Color,"Color", "F_Color"),
        new FieldTypeInfo(FieldType.Bool,"Bool", "F_Bool"),
        new FieldTypeInfo(FieldType.Enum,"LocalEnum.{0}", "F_Enum({0})"),
        new FieldTypeInfo(FieldType.EntityRef, "EntityRef", "F_EntityRef"),
        new FieldTypeInfo(FieldType.Multilines, "String", "F_Text"),
        new FieldTypeInfo(FieldType.FilePath, "FilePath", "F_Path"),
        new FieldTypeInfo(FieldType.Tile, "Tile", "F_Tile"),
        new FieldTypeInfo(FieldType.Point, "Point", "F_Point")
    };

    /// <summary>
    /// Returns default field type used to convert from GM to LDTK fields.
    /// </summary>
    public static FieldTypeInfo GetDefaultFieldTypeInfo( GMObjectProperty _property )
    {
        return _property.varType switch
        {
            eObjectPropertyType.Integer => GetFieldTypeInfo( FieldType.Int ),
            eObjectPropertyType.Real => GetFieldTypeInfo( FieldType.Float ),
            eObjectPropertyType.Color => GetFieldTypeInfo( FieldType.Color ),
            eObjectPropertyType.Boolean => GetFieldTypeInfo( FieldType.Bool ),
            eObjectPropertyType.List => GetFieldTypeInfo( FieldType.Enum ),
            _ => GetFieldTypeInfo( FieldType.String )
        };
    }

    public static FieldTypeInfo GetFieldTypeInfo( FieldType _type )
    {
        return FieldTypeInfoList[(int) _type];
    }

    private const string ArrayIdentifier = "Array<";

    public static FieldTypeInfo GetFieldTypeInfo( string __type )
    {
        if (__type.StartsWith(ArrayIdentifier))
        {
            var result = GetFieldTypeInfo(__type.Substring(ArrayIdentifier.Length, __type.Length - ArrayIdentifier.Length - 1));
            result.__type = __type;
            result.isArray = true;
            return result;
        }
        
        if ( __type.StartsWith( "LocalEnum." ) )
            return GetFieldTypeInfo( FieldType.Enum );

        return GetFieldTypeInfo( System.Enum.Parse<FieldType>( __type ) );
    }

    public static bool GM2LDTK( LDTKProject _project, string? _value, string _fieldType, GMObjectProperty _gmProp, out DefaultOverride?[]? _result, Dictionary<string, string>? _idMatches = null )
    {
        if (_value == null)
        {
            _result = null;
            return true;
        }

        var targetType =  GetFieldTypeInfo( _fieldType );

        DefaultOverride? GetValue( string _text )
        {
            switch ( targetType.enumType )
            {
                case FieldType.Bool:

                    bool boolValue = _text switch
                    {
                        "0" => false,
                        "1" => true,
                        _ => bool.Parse( _text )
                    };

                    return new DefaultOverride( DefaultOverride.IdTypes.V_Bool, boolValue );

                case FieldType.Int:
                    return new DefaultOverride( DefaultOverride.IdTypes.V_Int, int.Parse( _text ) );

                case FieldType.Float:
                    return new DefaultOverride( DefaultOverride.IdTypes.V_Float, float.Parse( _text ) );

                case FieldType.Enum:

                    var enumType = GetFieldEnum( _fieldType, _project );
                    if (enumType == null)
                        throw new Exception("Enum not found");

                    string enumValue;
                    switch (_gmProp.varType)
                    {
                        case eObjectPropertyType.Integer:
                            enumValue = enumType.values[int.Parse(_text)].id!;
                            break;
                        case eObjectPropertyType.List:
                            enumValue = enumType.values[_gmProp.listItems.IndexOf(_text)].id!;
                            break;
                        case eObjectPropertyType.String:
                        case eObjectPropertyType.Expression:
                            enumValue = _text;
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    return new DefaultOverride( DefaultOverride.IdTypes.V_String, enumValue );

                case FieldType.EntityRef:
                case FieldType.String:
                case FieldType.Multilines:

                    if ( _idMatches != null && _idMatches.TryGetValue( _text, out var otherId ) )
                        _text = otherId;

                    return new DefaultOverride( DefaultOverride.IdTypes.V_String, _text );

                default:
                    return new DefaultOverride( DefaultOverride.IdTypes.V_String, _text );
            }
        }

        DefaultOverride?[]? GetArrayValue( string _text )
        {
            if (_gmProp.varType != eObjectPropertyType.Expression)
                throw new NotSupportedException("Only Expression property type is valid for array fields");

            _text = _text.Trim(' ', '[', ']');
            var items = _text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            DefaultOverride[] result = new DefaultOverride[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var value = GetValue(items[i]);
                if (value == null )
                    continue;

                result[i] = value;
            }

            return result;
        }

        try
        {
            if (targetType.isArray)
            {
                _result = GetArrayValue(_value);
            }
            else
            {
                _result = new []{ GetValue(_value) };
            }

            return true;
        }
        catch ( NotSupportedException )
        {
            AnsiConsole.MarkupLineInterpolated( $"[red]Unable to convert value {_value} from {_gmProp.varType} to {targetType.enumType}[/]" );
            _result = new []{ new DefaultOverride( DefaultOverride.IdTypes.V_String, _value ) };
            return false;
        }
        catch ( Exception )
        {
            _result = new []{ new DefaultOverride( DefaultOverride.IdTypes.V_String, _value ) };
            return false;
        }
    }

    public static string? LDTK2GM( LDTKProject _project, object? _input, Field _ldtkProp, GMObjectProperty _gmProp, Dictionary<string, string> _idMatches )
    {
        if ( _input == null )
            return null;

        var fieldType = FieldConversion.GetFieldTypeInfo(_ldtkProp.__type);

        string? ConvertEntityRef( string? _value )
        {
            if (_value == null)
                return null;
            return _idMatches.GetValueOrDefault(_value, _value);
        }

        string? ConvertValue( object _value )
        {
            switch (fieldType.enumType)
            {
                case FieldType.Point:
                    
                    switch ( _gmProp.varType )
                    {
                        case eObjectPropertyType.Expression:
                        case eObjectPropertyType.String:
                            return _value.ToString();
                        default:
                            throw new NotSupportedException();
                    }
                    
                case FieldType.EntityRef:
                    
                    switch ( _gmProp.varType )
                    {
                        case eObjectPropertyType.Expression:
                        case eObjectPropertyType.String:
                            string? entityId;
                            if (_value is JsonElement elem && elem.ValueKind != JsonValueKind.String)
                                entityId = elem.GetProperty("entityIid").GetString();
                            else
                                entityId = _value.ToString();

                            return ConvertEntityRef(entityId);
                        
                        default:
                            throw new NotSupportedException();
                    }
                
                case FieldType.Enum:
                    
                    var enumType = GetFieldEnum( _ldtkProp.__type, _project );

                    if (enumType == null) 
                        return _value.ToString();
                    
                    var inputString = _value.ToString();
                    int valueIndex = enumType.values.FindIndex( t => t.id == inputString );

                    switch ( _gmProp.varType )
                    {
                        case eObjectPropertyType.Real:
                        case eObjectPropertyType.Integer:
                            return valueIndex.ToString();
                        case eObjectPropertyType.Boolean:
                            return valueIndex > 0 ? "true" : "false";
                        case eObjectPropertyType.Color:
                            throw new NotSupportedException("Colors are not supported yet");
                        //return valueIndex >= 0 ? enumType.values[valueIndex].color.ToString() : "";
                    }

                    return (uint) valueIndex < _gmProp.listItems.Count
                        ? _gmProp.listItems[valueIndex]
                        : "";

                default:
                    return _value.ToString();
                
            }
        }

        string? ConvertArray( object _value )
        {
            IEnumerable<object> GetElements()
            {
                switch (_value)
                {
                    case JsonElement jsonElem:
                        if (jsonElem.ValueKind != JsonValueKind.Array)
                            throw new Exception($"Unknown value type: {_value.GetType()}");

                        foreach (JsonElement element in jsonElem.EnumerateArray())
                            yield return element;
                    
                        break;
                    
                    case IEnumerable enm:

                        foreach (object o in enm)
                            yield return o;
                        
                        break;
                    
                    default:
                        throw new Exception($"Unknown value type: {_value.GetType()}");
                }
            }

            if (_gmProp.varType != eObjectPropertyType.Expression)
                throw new NotSupportedException();
            
            return $"[{string.Join(',', GetElements().Select( ConvertValue))}]";
        }
        
        try
        {
            if (fieldType.isArray)
                return ConvertArray(_input);
            return ConvertValue(_input);
        }
        catch ( NotSupportedException )
        {
            AnsiConsole.MarkupLineInterpolated( $"[red]Unable to convert value {_input} from {_ldtkProp.type} to {_gmProp.varType}[/]" );
        }
        catch ( Exception e )
        {
            AnsiConsole.MarkupInterpolated($"[red]{e}[/]");
        }

        return _input.ToString();
    }

    private static LDTKProject.Enum? GetFieldEnum( string _fieldType, LDTKProject _project )
    {
        if (!_fieldType.StartsWith("LocalEnum."))
            return null;
        var enumName = _fieldType.Substring( "LocalEnum.".Length );
        return _project.defs.enums.Find( t => t.identifier == enumName );
    }
}
