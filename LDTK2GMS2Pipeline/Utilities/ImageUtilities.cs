using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Advanced;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

public static class ImageUtilities
{
    /// <summary>
    /// Returns non-transparent area of the sprite. Same area GM crops its sprites
    /// </summary>
    public static Rectangle FindTrimRect( Image<Rgba32> _image )
    {
        int yMin = -1;
        int yMax = -1;

        int xMin = _image.Width - 1;
        int xMax = 0;

        _image.ProcessPixelRows( ( _accessor ) =>
        {
            for ( int y = 0; y < _accessor.Height; y++ )
            {
                int firstFilled = -1;
                int lastFilled = -1;

                Span<Rgba32> pixelRow = _accessor.GetRowSpan( y );
                for ( int x = 0; x < pixelRow.Length; x++ )
                {
                    ref Rgba32 pixel = ref pixelRow[x];
                    bool isEmpty = pixel.A == 0;
                    if ( isEmpty )
                        continue;

                    if ( firstFilled < 0 )
                        firstFilled = x;
                    lastFilled = x;
                }

                bool isEmptyRow = lastFilled < 0;
                if ( !isEmptyRow )
                {
                    if ( yMin < 0 )
                        yMin = y;
                    yMax = y;

                    xMin = Math.Min( firstFilled, xMin );
                    xMax = Math.Max( lastFilled, xMax );
                }
            }
        } );

        // Empty image. Return original rectangle
        if ( yMin < 0 )
            return new Rectangle( 0, 0, _image.Width, _image.Height );

        int width = xMax - xMin + 1;
        int height = yMax - yMin + 1;

        return new Rectangle( xMin, yMin, width, height );
    }

    public static void Trim( Image<Rgba32> _image )
    {
        Trim(_image, FindTrimRect(_image));
    }

    public static void Trim(Image<Rgba32> _image, Rectangle _rect)
    {
        _image.Mutate( _context => _context.Crop( _rect ) );
    }

    private static byte[]? _memoryCache;

    public static byte[] SaveToArray( this Image _image, IImageEncoder _encoder )
    {
        _memoryCache ??= new byte[2048 * 2048 * 5];

        using MemoryStream paletteStream = new( _memoryCache );
        _image.Save( paletteStream, _encoder );
        return paletteStream.ToArray();
    }

    public static byte[] SaveToArray( this Image _image )
    {
        return SaveToArray( _image, _image.GetConfiguration().ImageFormatsManager.GetEncoder( PngFormat.Instance ) );
    }
}