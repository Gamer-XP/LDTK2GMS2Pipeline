Tool allows to sync data between GMS2 and LDTK projects directly. It imports entities and levels from GM to LDTK first, then allows to export levels back to GM.

Made for LDTK [1.4.1 - 1.5.1] and GMS2 [2023.1.1.62]

Limitations:
- LDTK does not support tile rotation. Rotation flags will be lost
- Room inheritance-related data will likely be lost/overritten becasue there is no way to resolve it on LDTK side.
- Multi-select list fields are not supported by LDTK properly yet. They will be ignored
- You can input expressions in fields in GM, but they can't be resolved by LDTK. Such fields will be ignored when imported, but won't be lost when exporting back unless you override them in LDTK.
- LDTK have global layers, while GM got different layers per room. Tool will only import data from layers if they have same-named layer of matching type defined in LDTK
- Auto layers are not properly supported because you can't change tileset used by the auto-layer, plus you can't really import data back from GM.
- LDTK lacks ability to set sprite's pivot precisely like GM, so some entities may look a bit different in edge cases

Build notes:
- Required GMS2 DLL files to build the app. If GM is installed at the default path - project should find them just fine. This was not tested will all versions, so it may not work properly with newer/old IDE versions.

Initial setup steps:
- Organize rooms in your GM project to have same names for same-purpose layers as much as possible. LDTK can only work with fixed layers.
- Mark all objects you want to use in LDTK with "Room Asset" tag (can use different tag if you want, but will need to change it in .ini file later)
- Create LDTK project somwhere in sub-folders of your GM project.
- Add layers you will use to LDTK project.
- Compile and put tool's exe in the same folder as LDTK project.
- Create a .bat file with such content:

  {tool_exe_name}.exe --mode Import
  {your_project_name}.ldtk

  This will allows you to quickly import data from GM before starting LDTK

- Create a command in LDTK project (either OnAfterSave or Manual) with such content: {tool_exe_name}.exe --mode Export. Use it to export data to GM directly from LDTK.

Usage notes:
- Tool is not guranteed to not destroy your LDTK/GMS2 projects yet. Use version control system to back up your data.
- Tool imports ONLY entities with "Room Asset" tag, to prevent importing unrelated things. Tag used can be changed in .ini files that is generated after initial import.
- There is an .ini file that can be used to filted what fields need to be imported. Format is {ObjectName}: [{FieldName1}, {FieldName2} ]. You can add ~ before object name to invert filter, making it allow only listed fields instead of excluding them.
- Most imported things can be deleted after the import, and they won't be reimported later. If you want to reimport them - you need to delete them from the .meta file, or add them back manually.
- Most things can be safely renamed after the import. Their original names are stored in .meta file
- You can't move around imported enum's values - tool syncs values by their index. All other modifications are allowed.
- FlipState field is added automatically for all imported entities. It represents X/Y scale signs. You can remove the field if you won't need it.
- You can add "image_index" field of "int" type to any entity. After next import it will be replaced with enum field that has all sprite's subimages assigned.
- You can delete a field, then add a new one with same name of different type. Tool will try to convert data types between GM and LDTK formats when possible. RECOMMENDATION: After creating a field save the project, then import data from GM again. Else you may lose that field's original values.
- You can create level fields with "DEPTH_{LAYER_NAME}" name of INT type. This will allow you to import/export depth values for given layers
  
