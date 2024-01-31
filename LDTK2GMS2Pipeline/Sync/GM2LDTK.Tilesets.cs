using LDTK2GMS2Pipeline.LDTK;
using LDTK2GMS2Pipeline.Utilities;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline.Sync;

internal static partial class GM2LDTK
{
    /// <summary>
    /// Updates used tilesets
    /// </summary>
    private static void UpdateTilesets(LDTKProject _project, List<GMTileSet> _tilesets)
    {
        _project.RemoveUnusedMeta<LDTKProject.Tileset.MetaData>(_tilesets.Select(t => t.name).Append(SharedData.EntityAtlasName), _meta => { _project.Remove<LDTKProject.Tileset>(_meta.identifier); });

        foreach (GMTileSet gmTileset in _tilesets)
        {
            _project.CreateOrExisting<LDTKProject.Tileset>(gmTileset.name, out var tileset);
            if (tileset == null)
                continue;

            if (gmTileset.tilehsep != gmTileset.tilevsep)
                Log.Write($"[{Log.ColorError}]Error in [{Log.ColorAsset}]{gmTileset.name}[/]! Different spacing is not supported by LDTK: {gmTileset.tilehsep}, {gmTileset.tilevsep}[/]");

            if (gmTileset.tilexoff != gmTileset.tileyoff)
                Log.Write($"[{Log.ColorError}]Error in [{Log.ColorAsset}]{gmTileset.name}[/]! Different offsets are not supported by LDTK: {gmTileset.tilexoff}, {gmTileset.tileyoff}[/]");

            if (gmTileset.tilexoff != 0 || gmTileset.tileyoff != 0)
                Log.Write($"[{Log.ColorWarning}]Warning in [{Log.ColorAsset}]{gmTileset.name}[/]! Offsets work as padding in LDTK. You may lose most right and bottom tiles![/]");

            tileset.pxWid = gmTileset.spriteId.width;
            tileset.pxHei = gmTileset.spriteId.height;
            tileset.tileGridSize = gmTileset.tileWidth;
            // LDTK treats partially-filled cells as proper tiles, while GM ignores them
            tileset.__cWid = (gmTileset.spriteId.width - gmTileset.tilexoff + gmTileset.tilehsep + gmTileset.tileWidth + gmTileset.tilehsep - 2) / (gmTileset.tileWidth + gmTileset.tilehsep);
            tileset.__cHei = (gmTileset.spriteId.height - gmTileset.tileyoff + gmTileset.tilevsep + gmTileset.tileHeight + gmTileset.tilevsep - 2) / (gmTileset.tileHeight + gmTileset.tilevsep);
            tileset.spacing = gmTileset.tilehsep;
            tileset.padding = gmTileset.tilexoff;

            string tilesetFullPath = GMProjectUtilities.GetFullPath(gmTileset.spriteId.GetCompositePaths()[0]);
            tileset.relPath = Path.GetRelativePath(_project.ProjectDirectory, tilesetFullPath).Replace("\\", "/");
        }
    }
}