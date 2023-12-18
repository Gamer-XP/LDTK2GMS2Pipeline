using LDTK2GMS2Pipeline.LDTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal class SharedData
{
    public const string LevelObjectTag = "Room Asset";
    public const string IconFolder = "Generated";

    public const string EntityAtlasName = "EntityAtlas";

    public const string FlipStateEnumName = "FlipState";
    public const string ImageIndexState = "image_index";

    public static readonly GMObjectProperty FlipProperty = new()
    {
        varType = eObjectPropertyType.List,
        varName = FlipStateEnumName,
        value = "None",
        listItems = new ResourceList<string>() { "None", "Flip_X", "Flip_Y", "Flip_X_Y" }
    };

    public static readonly GMObjectProperty ImageIndexProperty = new()
    {
        varType = eObjectPropertyType.Integer,
        varName = ImageIndexState,
        value = "0"
    };

    public static LDTKProject.Enum GetFlipEnum( LDTKProject _ldtkProject )
    {
        if ( _ldtkProject.CreateOrExistingForced<LDTKProject.Enum>( FlipStateEnumName, out var result ) )
        {
            result.values = FlipProperty.listItems.Select( _s => new LDTKProject.Enum.Value() { id = _s } ).ToList();
        }

        return result;
    }
}
