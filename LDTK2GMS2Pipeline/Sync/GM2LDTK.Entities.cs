using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Utilities;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal static partial class GM2LDTK
{
    private static void UpdateEntities(List<GMObject> _objects, LDTKProject _ldtkProject, SpriteAtlas _atlas, LDTKProject.Tileset _atlasTileset)
    {
        //Log.EnableAutoLog = Log.AutoLogLevel.Off;
        
        var sortedList = _objects.OrderBy(t => t.name).ToList();
        sortedList.Sort((l, r) =>
        {
            if (GMProjectUtilities.IsInheritedFrom(l, r))
                return 1;
            if (GMProjectUtilities.IsInheritedFrom(r, l))
                return -1;
            return 0;
        });

        foreach (var levelObject in sortedList)
        {
            bool isNew = _ldtkProject.CreateOrExisting(levelObject.name, out LDTKProject.Entity? entity);
            if (entity == null) 
                continue;

            if (isNew)
            {
                Log.Push();
                entity.InitSprite(levelObject, _atlasTileset, _atlas);
            }
            else
                Log.PushResource(entity);

            //Log.EnableAutoLog = Log.AutoLogLevel.Auto;
            _ldtkProject.UpdateEntity(entity, levelObject);
            //Log.EnableAutoLog = Log.AutoLogLevel.Off;
            
            Log.Pop();
        }

        _ldtkProject.RemoveUnusedMeta<LDTKProject.Entity.MetaData>(_objects.Select(t => t.name), _s =>
        {
            _ldtkProject.Remove<LDTKProject.Entity>(_s.identifier);
        });
        
       // Log.EnableAutoLog = Log.AutoLogLevel.Auto;
    }

    private static HashSet<GMProjectUtilities.GMObjectPropertyInfo> loggedErrorsFor = new();
    private static HashSet<string> dupeMetaChecked = new HashSet<string>(16);

    public static void UpdateEntity(this LDTKProject _project, LDTKProject.Entity _entity, GMObject _object)
    {
        var flipEnum = SharedData.GetFlipEnum(_project);

        List<GMProjectUtilities.GMObjectPropertyInfo> definedProperties = LDTKProject.EnumerateAllProperties(_object).ToList();

        RemoveDupeMeta();
        RemoveMissingProperties(definedProperties.Select(t => t.Property));

        foreach (var propertyInfo in definedProperties)
        {
            if (!_project.Options.IsPropertyIgnored(propertyInfo.DefinedIn, propertyInfo.Property, _object))
            {
                if (propertyInfo.Property != SharedData.ImageIndexProperty)
                    UpdateField(propertyInfo);
                else
                    UpdateImageIndex();
            }
            else
                RemoveProperty(propertyInfo);
        }

        void RemoveMissingProperties(IEnumerable<GMObjectProperty> _properties)
        {
            _entity.RemoveUnusedMeta<LDTKProject.Field.MetaData>(_properties.Select(t => t.varName), _s =>
            {
                _entity.Remove<LDTKProject.Field>(_s.identifier);
            });
        }

        void RemoveDupeMeta()
        {
            dupeMetaChecked.Clear();
            var lst = _entity.GetMetaList(typeof(LDTKProject.Field.MetaData));
            for (int i = lst.Count - 1; i >= 0; i--)
            {
                var m = (LDTKProject.IMeta)lst[i];
                if (dupeMetaChecked.Add(m.identifier))
                    continue;

                lst.Remove(m);
                Log.Write($"[{Log.ColorWarning}]Removed duplicate meta with name [underline]{m.identifier}[/] in entity [{Log.ColorEntity}]{_object.name}[/].[/]");
            }
        }

        void UpdateImageIndex()
        {
            LDTKProject.Field? field = _entity.GetResource<LDTKProject.Field>(SharedData.ImageIndexState);
            if (field == null)
                return;

            if (field.Meta == null)
                _entity.CreateMetaFor(field, SharedData.ImageIndexState);

            if (field.__type == "Int" && _object.spriteId != null)
            {
                // Try convert to enum field
                var spriteEnum = _project.GetResource<LDTKProject.Enum>(_object.spriteId.name);
                if (spriteEnum == null)
                    return;

                field.__type = $"LocalEnum.{spriteEnum.identifier}";
                field.type = $"F_Enum({spriteEnum.uid})";
                field.editorDisplayMode = "EntityTile";
                var firstValue = spriteEnum.values.Count > 0 ? spriteEnum.values[0].id : null;
                field.defaultOverride = firstValue != null ? new LDTKProject.DefaultOverride(LDTKProject.DefaultOverride.IdTypes.V_String, firstValue) : null;
            }
        }

        void UpdateField(GMProjectUtilities.GMObjectPropertyInfo _info)
        {
            if (_info.Property.multiselect)
            {
                Log.Write($"[{Log.ColorWarning}]Field [{Log.ColorField}]{_info.Property.varName}[/] will be ignored. Multi-Select fields are not supported yet.[/]");
                return;
            }

            FieldConversion.FieldTypeInfo fieldType;
            LDTKProject.Field? field = _entity.GetResource<LDTKProject.Field>(_info.Property.varName);
            if (field != null)
            {
                fieldType = FieldConversion.GetFieldTypeInfo(field.__type);
            }
            else
            {
                fieldType = FieldConversion.GetDefaultFieldTypeInfo(_info.Property);
            }

            bool isEnum = _info.Property.varType == eObjectPropertyType.List;
            if (isEnum)
            {
                InitializeListProperty(_info, out LDTKProject.Enum en);

                fieldType.__type = string.Format(fieldType.__type, en.identifier);
                fieldType.type = string.Format(fieldType.type, en.uid);
            }

            string defaultValue = GMProjectUtilities.GetDefaultValue(_object, _info.Property);

            bool convertSuccess = FieldConversion.GM2LDTK(_project, defaultValue, fieldType.__type, _info.Property, out LDTKProject.DefaultOverride[]? defaultValueJson);

            if (!convertSuccess && !loggedErrorsFor.Contains(_info))
            {
                Log.Write($"[{Log.ColorWarning}]Field [{Log.ColorField}]{_info.Property.varName} [[{_info.Property.varType}]][/] - unable to process value '{defaultValue}'.[/]");
                loggedErrorsFor.Add(_info);
            }

            bool justCreated;
            if (field == null)
                justCreated = _entity.CreateOrExisting<LDTKProject.Field>(_info.Property.varName, out field);
            else
            {
                if (field.Meta == null)
                    _entity.CreateMetaFor(field, _info.Property.varName);
                justCreated = false;
            }

            if (field == null)
                return;

            var firstValue = defaultValueJson != null && defaultValueJson.Length > 0 ? defaultValueJson[0] : null;

            if (justCreated)
            {
                field.type = fieldType.type;
                field.__type = fieldType.__type;
                field.canBeNull = false;
                field.editorShowInWorld = true;
                field.editorDisplayMode = "NameAndValue";
                field.editorDisplayPos = "Beneath";
                field.defaultOverride = firstValue;
                field.isArray = _info.Property.multiselect;

                if (_info.Property.rangeEnabled)
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
                        Log.Write($"Field [{Log.ColorField}]{field.identifier}[/] - value changed from '{field.defaultOverride?.ToString()}' to '{firstValue?.ToString()}'");
                    field.defaultOverride = firstValue;
                }
            }

            field.Meta.gotError = !convertSuccess;
        }

        void RemoveProperty(GMProjectUtilities.GMObjectPropertyInfo _info)
        {
            _entity.Remove<LDTKProject.Field>(_info.Property.varName, true);
        }

        void InitializeListProperty(GMProjectUtilities.GMObjectPropertyInfo _info, out LDTKProject.Enum _enum)
        {
            if (_info.Property.varName == SharedData.FlipStateEnumName)
            {
                _enum = flipEnum;
                return;
            }

            string enumName = ProduceEnumName(_info.DefinedIn, _info.Property);

            _project.CreateOrExistingForced(enumName, out _enum);

            _enum.ValidateValues(_info.Property);
        }

        static string ProduceEnumName(GMObject _object, GMObjectProperty _property)
        {
            return $"{_object.name}_{_property.varName}";
        }
    }
}