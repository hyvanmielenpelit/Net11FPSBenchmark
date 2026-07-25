namespace Net11FPSBenchmark;

/// <summary>
/// Constants matching GnollHack's rendering pipeline exactly.
/// These values are copied from GnollHackX/GHConstants.cs.
/// </summary>
public static class Constants
{
    /* Tile dimensions (pixels) */
    public const int TileWidth = 64;
    public const int TileHeight = 96;

    /* Tile sheet layout */
    public const int TilesPerRow = 128;             // MaxTileSheetWidthInTiles
    public const int TilesPerSheet = 8192;          // NumberOfTilesPerSheet

    /* Map grid dimensions */
    public const int MapCols = 80;                  // COLNO in global.h
    public const int MapRows = 21;                  // ROWNO in global.h

    /* Rendering layers — GnollHack uses 20 core layers + 2 extra passes */
    public const int MaxLayers = 20;
    public const int TotalRenderPasses = 22;        // 20 layers + shadow + UI overlay

    /* Tile sheet pixel dimensions: 128 tiles × 64px = 8192px wide,
       8192/128 = 64 rows × 96px = 6144px tall */
    public const int TileSheetWidthPx = TilesPerRow * TileWidth;    // 8192
    public const int TileSheetHeightPx = (TilesPerSheet / TilesPerRow) * TileHeight; // 6144

    /* Benchmark display */
    public const float DefaultZoom = 1.5f;          // Normal view zoom factor
}
