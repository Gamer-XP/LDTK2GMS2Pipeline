using System.Collections;
using Spectre.Console;
using System.Text.RegularExpressions;
using YoYoStudio.Resources;
using static LDTK2GMS2Pipeline.Utilities.GMProjectUtilities;
using System.Reflection;
using LDTK2GMS2Pipeline.Sync;
using Microsoft.VisualBasic.FileIO;
using LDTK2GMS2Pipeline.Utilities;
using static LDTK2GMS2Pipeline.LDTK.LDTKProject.Level;
using System.Text.Json;

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

            bool use9Slice = _object.spriteId?.nineSlice?.enabled ?? false;

            if ( use9Slice )
            {
                tileRect.x += rect.PaddingLeft;
                tileRect.y += rect.PaddingTop;
                tileRect.w -= rect.PaddingLeft + rect.PaddingRight;
                tileRect.h -= rect.PaddingTop + rect.PaddingBottom;
                width = tileRect.w;
                height = tileRect.h;
            }

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
        
        public List<T> GetResourceList<T>()
            where T : IResource
        {
            return (List<T>) GetResourceList( typeof( T ) );
        }

        public List<T> GetMetaList<T>()
            where T : IMeta
        {
            return (List<T>) GetMetaList( typeof( T ) );
        }
    }

    public sealed class Field : Resource<Field.MetaData>
    {
        public class MetaData : Meta<Field>
        {
            public string? type { get; set; }
            public bool gotError { get; set; }
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
        yield return new GMObjectPropertyInfo( SharedData.ImageIndexProperty, _object );

        foreach ( GMObjectPropertyInfo info in GMProjectUtilities.EnumerateAllProperties( _object ) )
        {
            yield return info;
        }
    }

    private static HashSet<GMObjectPropertyInfo> loggedErrorsFor = new();
    private static HashSet<string> dupeMetaChecked = new HashSet<string>(16);

    public void UpdateEntity( Entity _entity, GMObject _object )
    {
        var flipEnum = SharedData.GetFlipEnum( this );

        List<GMObjectPropertyInfo> definedProperties = EnumerateAllProperties( _object ).ToList();

        RemoveDupeMeta();
        RemoveMissingProperties( definedProperties.Select( t => t.Property ) );

        foreach ( var propertyInfo in definedProperties )
        {
            if (!Options.IsPropertyIgnored(propertyInfo.DefinedIn, propertyInfo.Property, _object))
            {
                if ( propertyInfo.Property != SharedData.ImageIndexProperty )
                    UpdateField(propertyInfo);
                else
                    UpdateImageIndex();
            }
            else
                RemoveProperty(propertyInfo);
        }

        void RemoveMissingProperties( IEnumerable<GMObjectProperty> _properties )
        {
            _entity.RemoveUnusedMeta<Field.MetaData>( _properties.Select( t => t.varName), _s =>
            {
                _entity.Remove<Field>(_s.identifier);
                AnsiConsole.MarkupLineInterpolated( $"[blue]Property [underline]{_s.identifier}[/] in object [teal]{_object.name}[/] is missing. Removing...[/]" );
            } );
        }

        void RemoveDupeMeta()
        {
            dupeMetaChecked.Clear();
            var lst = _entity.GetMetaList( typeof( Field.MetaData ) );
            for ( int i = lst.Count - 1; i >= 0; i-- )
            {
                var m = (IMeta) lst[i];
                if ( dupeMetaChecked.Add( m.identifier ) )
                    continue;

                lst.Remove( m );
                AnsiConsole.MarkupLineInterpolated( $"[yellow]Removed duplicate meta with name [underline]{m.identifier}[/] in object [teal]{_object.name}[/].[/]" );
            }
        }

        void UpdateImageIndex()
        {
            Field? field = _entity.GetResource<Field>( SharedData.ImageIndexState );
            if (field == null)
                return;

            if ( field.Meta == null )
                _entity.CreateMetaFor( field, SharedData.ImageIndexState );

            if (field.__type == "Int" && _object.spriteId != null )
            {
                // Try convert to enum field
                var spriteEnum = this.GetResource<LDTKProject.Enum>(_object.spriteId.name);
                if (spriteEnum == null)
                    return;

                field.__type = $"LocalEnum.{spriteEnum.identifier}";
                field.type = $"F_Enum({spriteEnum.uid})";
                field.editorDisplayMode = "EntityTile";
                var firstValue = spriteEnum.values.Count > 0? spriteEnum.values[0].id : null;
                field.defaultOverride = firstValue != null? new DefaultOverride(DefaultOverride.IdTypes.V_String, firstValue ) : null;
            }
        }

        void UpdateField( GMObjectPropertyInfo _info )
        {
            if ( _info.Property.multiselect )
            {
                AnsiConsole.MarkupLineInterpolated( $"[yellow]Multi-Select fields are not supported by LDTK yet. [green]{_info.Property.varName}[/] in [teal]{_object.name}[/] will be ignored.[/]" );
                return;
            }

            FieldConversion.FieldTypeInfo fieldType;
            Field? field = _entity.GetResource<Field>(_info.Property.varName);
            if (field != null)
            {
                fieldType = FieldConversion.GetFieldTypeInfo(field.__type);
            }
            else
            {
                fieldType = FieldConversion.GetDefaultFieldTypeInfo( _info.Property );
            }

            bool isEnum = _info.Property.varType == eObjectPropertyType.List;
            if ( isEnum )
            {
                InitializeListProperty( _info, out LDTKProject.Enum en );

                fieldType.__type = string.Format( fieldType.__type, en.identifier );
                fieldType.type = string.Format( fieldType.type, en.uid );
            }

            string defaultValue = GetDefaultValue( _object, _info.Property );

            bool convertSuccess = FieldConversion.GM2LDTK( this, defaultValue, fieldType.__type, _info.Property, out DefaultOverride[]? defaultValueJson );

            if ( !convertSuccess && !loggedErrorsFor.Contains( _info ) )
            {
                AnsiConsole.MarkupLineInterpolated( $"[yellow]Error processing value '{defaultValue}' for field [green]{_info.Property.varName} [[{_info.Property.varType}]][/] in [teal]{_object.name}[/].[/]" );
                loggedErrorsFor.Add( _info );
            }

            bool justCreated;
            if (field == null)
                justCreated = _entity.CreateOrExisting<Field>(_info.Property.varName, out field);
            else
            {
                if (field.Meta == null)
                    _entity.CreateMetaFor(field, _info.Property.varName);
                justCreated = false;
            }

            if ( field == null)
                return;

            var firstValue = defaultValueJson != null && defaultValueJson.Length > 0 ? defaultValueJson[0] : null;

            if ( justCreated )
            {
                field.type = fieldType.type;
                field.__type = fieldType.__type;
                field.canBeNull = false;
                field.editorShowInWorld = true;
                field.editorDisplayMode = "NameAndValue";
                field.editorDisplayPos = "Beneath";
                field.defaultOverride = firstValue;
                field.isArray = _info.Property.multiselect;

                if ( _info.Property.rangeEnabled )
                {
                    field.min = _info.Property.rangeMin;
                    field.max = _info.Property.rangeMax;
                }
            }
            else
            {
                if (!object.Equals(firstValue, field.defaultOverride))
                {
                    if (!field.Meta.gotError)
                        AnsiConsole.MarkupLineInterpolated(
                            $"Default Value changed for field [green]{field.identifier}[/] in [teal]{_entity.identifier}[/] from '{field.defaultOverride?.ToString()}' to '{firstValue?.ToString()}'");
                    field.defaultOverride = firstValue;
                }
            }

            field.Meta.gotError = !convertSuccess;
        }

        void RemoveProperty( GMObjectPropertyInfo _info  )
        {
            _entity.Remove<Field>(_info.Property.varName, true);
        }

        void InitializeListProperty( GMObjectPropertyInfo _info, out LDTKProject.Enum _enum )
        {
            if ( _info.Property.varName == SharedData.FlipStateEnumName )
            {
                _enum = flipEnum;
                return;
            }

            string enumName = ProduceEnumName( _info.DefinedIn, _info.Property );

            this.CreateOrExistingForced( enumName, out _enum );

            _enum.ValidateValues( _info.Property );
        }

        static string ProduceEnumName( GMObject _object, GMObjectProperty _property )
        {
            return $"{_object.name}_{_property.varName}";
        }
    }
}