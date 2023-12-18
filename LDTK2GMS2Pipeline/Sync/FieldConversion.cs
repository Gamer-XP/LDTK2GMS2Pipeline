using LDTK2GMS2Pipeline.LDTK;
using Spectre.Console;
using System;
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

    public record FieldTypeInfo( FieldType enumType, string __type, string type );

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

    public static FieldTypeInfo GetFieldTypeInfo( string __type )
    {
        if ( __type.StartsWith( "LocalEnum." ) )
            return GetFieldTypeInfo( FieldType.Enum );

        return GetFieldTypeInfo( System.Enum.Parse<FieldType>( __type ) );
    }

    public static bool GM2LDTK( LDTKProject _project, string? _value, string _fieldType, GMObjectProperty _gmProp, out DefaultOverride? _result, Dictionary<string, string>? _idMatches = null )
    {
        if (_value == null)
        {
            _result = null;
            return true;
        }

        var targetType =  GetFieldTypeInfo( _fieldType );

        DefaultOverride? GetValue()
        {
            switch ( targetType.enumType )
            {
                case FieldType.Bool:

                    bool boolValue = _value switch
                    {
                        "0" => false,
                        "1" => true,
                        _ => bool.Parse( _value )
                    };

                    return new DefaultOverride( DefaultOverride.IdTypes.V_Bool, boolValue );

                case FieldType.Int:
                    return new DefaultOverride( DefaultOverride.IdTypes.V_Int, int.Parse( _value ) );

                case FieldType.Float:
                    return new DefaultOverride( DefaultOverride.IdTypes.V_Float, float.Parse( _value ) );

                case FieldType.Enum:

                    var enumType = GetFieldEnum( _fieldType, _project );
                    if (enumType == null)
                        throw new Exception("Enum not found");

                    string enumValue;
                    switch (_gmProp.varType)
                    {
                        case eObjectPropertyType.Integer:
                            enumValue = enumType.values[int.Parse(_value)].id!;
                            break;
                        case eObjectPropertyType.List:
                            enumValue = enumType.values[_gmProp.listItems.IndexOf(_value)].id!;
                            break;
                        case eObjectPropertyType.String:
                        case eObjectPropertyType.Expression:
                            enumValue = _value;
                            break;
                        default:
                            throw new NotSupportedException();
                    }

                    return new DefaultOverride( DefaultOverride.IdTypes.V_String, enumValue );

                case FieldType.EntityRef:
                case FieldType.String:
                case FieldType.Multilines:

                    if ( _idMatches != null && _idMatches.TryGetValue( _value, out var otherId ) )
                        _value = otherId;

                    return new DefaultOverride( DefaultOverride.IdTypes.V_String, _value );

                default:
                    return new DefaultOverride( DefaultOverride.IdTypes.V_String, _value );
            }
        }

        try
        {
            _result = GetValue();

            return true;
        }
        catch ( NotSupportedException )
        {
            AnsiConsole.MarkupLineInterpolated( $"[red]Unable to convert value {_value} from {_gmProp.varType} to {targetType.enumType}[/]" );
            _result = new DefaultOverride( DefaultOverride.IdTypes.V_String, _value );
            return false;
        }
        catch ( Exception )
        {
            _result = new DefaultOverride( DefaultOverride.IdTypes.V_String, _value );
            return false;
        }
    }

    public static string? LDTK2GM( LDTKProject _project, object? _input, Field _ldtkProp, GMObjectProperty _gmProp, Dictionary<string, string> _idMatches )
    {
        if ( _input == null )
            return null;

        string? ConvertValue()
        {
            if ( _ldtkProp.isArray )
            {
                throw new NotSupportedException();
            }

            if ( _ldtkProp.__type == "Point" )
            {
                switch ( _gmProp.varType )
                {
                    case eObjectPropertyType.Expression:
                    case eObjectPropertyType.String:
                        break;
                    default:
                        throw new NotSupportedException();
                }

                return null;
            }

            if ( _ldtkProp.__type == "EntityRef" )
            {
                switch ( _gmProp.varType )
                {
                    case eObjectPropertyType.Expression:
                    case eObjectPropertyType.String:
                        break;
                    default:
                        throw new NotSupportedException();
                }

                if ( _input is JsonElement elem )
                {
                    return elem.GetProperty( "entityIid" ).GetString();
                }

                return null;
            }

            var enumType = GetFieldEnum( _ldtkProp.__type, _project );

            if ( enumType != null )
            {
                var inputString = _input.ToString();
                int valueIndex = enumType.values.FindIndex( t => t.id == inputString );

                switch ( _gmProp.varType )
                {
                    case eObjectPropertyType.Real:
                    case eObjectPropertyType.Integer:
                        return valueIndex.ToString();
                    case eObjectPropertyType.Boolean:
                        return valueIndex > 0 ? "true" : "false";
                    case eObjectPropertyType.Color:
                        throw new NotSupportedException();
                        //return valueIndex >= 0 ? enumType.values[valueIndex].color.ToString() : "";
                }

                return (uint) valueIndex < _gmProp.listItems.Count
                    ? _gmProp.listItems[valueIndex]
                    : "";
            }

            return null;
        }

        string? result = null;

        try
        {
            result = ConvertValue();
        }
        catch ( NotSupportedException )
        {
            AnsiConsole.MarkupLineInterpolated( $"[red]Unable to convert value {_input} from {_ldtkProp.type} to {_gmProp.varType}[/]" );
        }
        catch ( Exception )
        {
            // ignored
        }

        if ( result == null )
        {
            result = _input.ToString();
        }

        if ( result != null && _idMatches.TryGetValue( result, out var entityReference ) )
            result = entityReference;

        return result;
    }

    private static LDTKProject.Enum? GetFieldEnum( string _fieldType, LDTKProject _project )
    {
        if (!_fieldType.StartsWith("LocalEnum."))
            return null;
        var enumName = _fieldType.Substring( "LocalEnum.".Length );
        return _project.defs.enums.Find( t => t.identifier == enumName );
    }
}
