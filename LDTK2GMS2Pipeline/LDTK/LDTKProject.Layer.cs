using Spectre.Console;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public static class LayerTypes
    {
        public const string IntGrid = nameof(IntGrid);
        public const string Entities = nameof( Entities );
        public const string Tiles = nameof( Tiles );
        public const string AutoLayer = nameof( AutoLayer );
    }

    public class Layer : Resource<Layer.MetaData>
    {
        public class MetaData : Meta<Layer> { }

        public string __type { get; set; }
        public string type { get; set; }
        public object doc { get; set; }
        public object uiColor { get; set; }
        public int gridSize { get; set; } = 16;
        public int guideGridWid { get; set; }
        public int guideGridHei { get; set; }
        public float displayOpacity { get; set; } = 1f;
        public float inactiveOpacity { get; set; } = 0.6f;
        public bool hideInList { get; set; }
        public bool hideFieldsWhenInactive { get; set; } = true;
        public bool canSelectWhenInactive { get; set; } = true;
        public bool renderInWorldView { get; set; } = true;
        public int pxOffsetX { get; set; }
        public int pxOffsetY { get; set; }
        public int parallaxFactorX { get; set; }
        public int parallaxFactorY { get; set; }
        public bool parallaxScaling { get; set; } = true;
        public List<string> requiredTags { get; set; } = new();
        public List<string> excludedTags { get; set; } = new();
        public List<object> intGridValues { get; set; } = new();
        public List<object> intGridValuesGroups { get; set; } = new();
        public List<object> autoRuleGroups { get; set; } = new();
        public object autoSourceLayerDefUid { get; set; }
        public int? tilesetDefUid { get; set; }
        public int tilePivotX { get; set; }
        public int tilePivotY { get; set; }

        public class IntGridValue
        {
            public int value { get; set; }
            public string identifier { get; set; }
            public string color { get; set; }
            public object tile { get; set; }
            public int groupUid { get; set; }
        }

        public class AutoRuleGroup
        {
            public int uid { get; set; }
            public string name { get; set; }
            public object color { get; set; }
            public object icon { get; set; }
            public bool active { get; set; }
            public bool isOptional { get; set; }
            public List<Rule> rules { get; set; } = new();
            public bool usesWizard { get; set; }
        }

        public class Rule
        {
            public int uid { get; set; }
            public bool active { get; set; } = true;
            public int size { get; set; }
            public List<int> tileIds { get; set; }
            public int alpha { get; set; }
            public double chance { get; set; }
            public bool breakOnMatch { get; set; }
            public List<int> pattern { get; set; }
            public bool flipX { get; set; }
            public bool flipY { get; set; }
            public int xModulo { get; set; }
            public int yModulo { get; set; }
            public int xOffset { get; set; }
            public int yOffset { get; set; }
            public int tileXOffset { get; set; }
            public int tileYOffset { get; set; }
            public int tileRandomXMin { get; set; }
            public int tileRandomXMax { get; set; }
            public int tileRandomYMin { get; set; }
            public int tileRandomYMax { get; set; }
            public string checker { get; set; }
            public string tileMode { get; set; }
            public double pivotX { get; set; }
            public double pivotY { get; set; }
            public int? outOfBoundsValue { get; set; }
            public bool perlinActive { get; set; }
            public int perlinSeed { get; set; }
            public double perlinScale { get; set; }
            public int perlinOctaves { get; set; }
        }

        public static bool CanBeConverted( GMRLayer _layer, string _type )
        {
            switch ( _layer )
            {
                case GMRInstanceLayer:
                    return _type == LayerTypes.Entities;

                case GMRTileLayer:
                    return _type == LayerTypes.Tiles;// || _type == LayerTypes.AutoLayer || _type == LayerTypes.IntGrid;

                default:
                    return false;
            }
        }
    }
}