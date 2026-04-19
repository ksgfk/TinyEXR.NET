namespace TinyEXR
{
    public sealed class ExrTileDescription
    {
        public int TileSizeX { get; set; }

        public int TileSizeY { get; set; }

        public ExrTileLevelMode LevelMode { get; set; }

        public ExrTileRoundingMode RoundingMode { get; set; }
    }
}
