using System.ComponentModel;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline;

public static class TilemapResizer
{
    public class Options
    {
        public int ColumnsPrevious;
        public int RowsPrevious;

        public int ColumnsNew;
        public int RowsNew;

        public bool AlignLeft = true;
        public bool AlignTop = true;

        public int OffsetX = 0;
        public int OffsetY = 0;

        public int FinalOffsetX { get; private set; }
        public int FinalOffsetY { get; private set; }

        public void Validate()
        {
            if (ColumnsPrevious == ColumnsNew && RowsPrevious == RowsNew)
                throw new WarningException("Current and New sizes are the same. No need to change anything.");

            if (ColumnsPrevious <= 0 || RowsPrevious <= 0 || ColumnsNew <= 0 || RowsNew <= 0)
                throw new Exception("Sizes can't be equal or below 0");

            int sizeDiffX = ColumnsNew - ColumnsPrevious;
            int sizeDiffY = RowsNew - RowsPrevious;

            FinalOffsetX = OffsetX;
            FinalOffsetY = OffsetY;
            if ( !AlignLeft )
                FinalOffsetX += sizeDiffX;
            if ( !AlignTop )
                FinalOffsetY += sizeDiffY;
        }
    }

    public static void Resize( GMProject _project, GMTileSet _tileSet, Options _options )
    {
        _options.Validate();

        foreach (GMRoom room in _project.GetResourcesByType<GMRoom>().Cast<GMRoom>())
        {
            foreach (GMRTileLayer layer in room.layers.Where( t => t is GMRTileLayer).Cast<GMRTileLayer>())
            {
                if ( layer.tilesetId == _tileSet)
                    Resize(layer, _options);
            }
        }
    }

    private static void Resize(GMRTileLayer _layer, Options _options)
    {
        var tiles = _layer.tiles.Tiles;

        for (int j = tiles.GetLength( 1 ) - 1; j >= 0; j--)
        for (int i = tiles.GetLength( 0 ) - 1; i >= 0; i--)
        {
            ref uint tile = ref tiles[i, j];
            uint index = TileMap.GetTileIndex(tile);
            if ( index == 0)
                continue;

            var oldX = index % _options.ColumnsPrevious;
            var oldY = index / _options.ColumnsPrevious;

            var newX = oldX + _options.FinalOffsetX;
            var newY = oldY + _options.FinalOffsetY;

            index = (uint)(newX + newY * _options.ColumnsNew);

            tile = (tile & ~TileMap.TileBitMask_TileIndex) | index;
        }


        _layer.tiles.Tiles = tiles;
    }

    public static void GetTilesetSize( GMTileSet _tileSet, out int _columns, out int _rows )
    {
        _columns = (_tileSet.spriteId.width - _tileSet.tilexoff + _tileSet.tilehsep) / (_tileSet.tileWidth + _tileSet.tilehsep);
        _rows = (_tileSet.spriteId.height - _tileSet.tileyoff + _tileSet.tilevsep) / (_tileSet.tileHeight + _tileSet.tilevsep);
    }
}
