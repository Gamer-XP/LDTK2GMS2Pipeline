namespace LDTK2GMS2Pipeline.LDTK;

public partial class LDTKProject
{
    public sealed class Tileset : Resource<Tileset.MetaData>
    {
        public class MetaData : Meta<Tileset> { }

        public int __cWid { get; set; }
        public int __cHei { get; set; }
        public string relPath { get; set; }
        public object embedAtlas { get; set; }
        public int pxWid { get; set; }
        public int pxHei { get; set; }
        public int tileGridSize { get; set; }
        public int spacing { get; set; }
        public int padding { get; set; }
        public List<string> tags { get; set; } = new ();
        public int? tagsSourceEnumUid { get; set; }
        public List<object> enumTags { get; set; } = new List<object>();
        public List<object> customData { get; set; } = new List<object>();
        public List<object> savedSelections { get; set; } = new List<object>();
        public CachedPixelData cachedPixelData { get; set; } = new CachedPixelData();

        public class CachedPixelData
        {
            public string opaqueTiles { get; set; } = string.Empty;
            public string averageColors { get; set; } = string.Empty;
        }
    }
}