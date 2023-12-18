using RectpackSharp;
using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline;

public class SpriteAtlas
{
    private readonly FileInfo imageFile, metaFile;
    private int cellSize;

    public FileInfo ImageFile => imageFile;
    public FileInfo MetaFile => metaFile;

    public int Width { get; private set; }
    public int Height { get; private set; }

    private DateTime? lastAtlasUpdateTime;

    public bool IsNew => lastAtlasUpdateTime == null;

    private Dictionary<string, AtlasItem> items = new();

    public interface IAtlasItem
    {
        public GMSprite Sprite { get; }
        public int ImageIndex { get; }
        public AtlasRectangle? PreviousRectangle { get; }
        public AtlasRectangle Rectangle { get; }
    }

    private class AtlasItem : IAtlasItem
    {
        public GMSprite Sprite { get; }
        public int ImageIndex { get; }
        public Image<Rgba32>? AtlasImage { get; set; }
        public AtlasRectangle? PreviousRectangle { get; set; } = null;
        public AtlasRectangle Rectangle { get; set; } = new();
        public bool NoCropping => Sprite.nineSlice?.enabled ?? false;
        public bool IsModified = false;
        public FileInfo ImagePath { get; }

        public AtlasItem( GMSprite _sprite, int _imageIndex = 0 )
        {
            Sprite = _sprite;
            ImageIndex = _imageIndex;
            var path = System.IO.Path.Combine( Path.GetDirectoryName( ProjectInfo.GetProjectPath( Sprite.project ) ), Sprite.GetCompositePaths()[_imageIndex] );
            ImagePath = new FileInfo( path );
        }
    }

    public SpriteAtlas( string _filePath, int _cellSize = 16 )
    {
        imageFile = new FileInfo( _filePath );
        metaFile = new FileInfo( Path.ChangeExtension( _filePath, ".meta" ) );

        cellSize = _cellSize;

        lastAtlasUpdateTime = imageFile.Exists ? imageFile.LastWriteTimeUtc : null;
    }

    private static string GetKey( GMSprite _sprite, int _imageIndex )
    {
        if (_imageIndex == 0)
            return _sprite.name;

        return $"{_sprite.name}#{_imageIndex}";
    }

    public void Add( GMSprite _sprite, bool _requireAllFrames = false )
    {
        Debug.Assert( _sprite != null );

        int count = _requireAllFrames ? _sprite.frames.Count : 1;
        for (int i = 0; i < count; i++)
        {
            string key = GetKey(_sprite, i);

            if ( !items.TryGetValue( key, out var info ) )
            {
                info = new AtlasItem( _sprite, i );
                items.Add( key, info );
            }

            info.IsModified |= IsSpriteUpdateNeeded( info );
        }
    }

    public void Add( IEnumerable<GMSprite> _sprites )
    {
        foreach ( GMSprite sprite in _sprites )
            Add( sprite );
    }

    /// <summary>
    /// Returns item for sprite with given name
    /// </summary>
    public IAtlasItem? Get( string? _name )
    {
        return _name == null ? null : items.GetValueOrDefault( _name );
    }

    public IAtlasItem? Get(GMSprite _sprite, int _imageIndex = 0)
    {
        return Get(GetKey(_sprite, _imageIndex));
    }

    /// <summary>
    /// Returns item that has given rectangle match or contain inside of it
    /// </summary>
    public IAtlasItem? Get( Rectangle _rect, bool _checkPrevious )
    {
        foreach ( AtlasItem item in items.Values )
        {
            AtlasRectangle rect;
            if ( _checkPrevious )
            {
                if ( item.PreviousRectangle == null )
                    continue;

                rect = item.PreviousRectangle;
            }
            else
            {
                rect = item.Rectangle;
            }

            if ( rect.Contains( _rect ) )
                return item;
        }

        return null;
    }

    /// <summary>
    /// Returns new position for given source rectangle, after atlas is updated
    /// </summary>
    public Rectangle? UpdatePosition( Rectangle _rect )
    {
        var item = Get( _rect, true );
        if ( item == null )
            return null;

        var prev = item.PreviousRectangle!;
        var left = _rect.Left - prev.Left;
        var top = _rect.Top - prev.Top;
        var right = _rect.Right - prev.Right;
        var bottom = _rect.Bottom - prev.Bottom;

        return new Rectangle( item.Rectangle.X + left, item.Rectangle.Y + top, item.Rectangle.Width - right - left, item.Rectangle.Height - top - bottom );
    }

    bool IsSpriteUpdateNeeded( AtlasItem _item )
    {
        if ( lastAtlasUpdateTime == null )
            return true;

        DateTime lastModified = _item.ImagePath.LastWriteTimeUtc;
        return lastModified > lastAtlasUpdateTime.Value;
    }

    private bool NeedRegeneration( AtlasMeta _meta )
    {
        if ( !imageFile.Exists || !metaFile.Exists || items.Any( t => t.Value.IsModified ) )
            return true;

        HashSet<string> keys = new HashSet<string>( _meta.Items.Keys );
        bool spriteListsMatch = keys.SetEquals( items.Keys );

        return !spriteListsMatch;
    }

    public async Task<bool> Update()
    {
        var meta = await LoadMeta();

        if ( !NeedRegeneration( meta ) )
        {
            ApplyMeta( meta );
            Console.WriteLine( "Atlas is up to date." );
            return false;
        }

        await LoadImagesFromAtlas( meta );
        await UpdateImages();

        var atlas = PackAtlas();
        Width = atlas.Width;
        Height = atlas.Height;

        Directory.CreateDirectory( imageFile.DirectoryName );
        await atlas.SaveAsync( imageFile.FullName );
        await SaveMeta();

        Console.WriteLine( "Atlas updated." );

        return true;
    }

    private Image<Rgba32> PackAtlas()
    {
        PackingRectangle[] rectangles = new PackingRectangle[items.Count];
        AtlasItem[] spritList = items.Values.ToArray();
        for ( int i = spritList.Length - 1; i >= 0; i-- )
        {
            var info = spritList[i];
            rectangles[i] = new PackingRectangle( 0, 0, (uint) info.AtlasImage!.Width, (uint) info.AtlasImage.Height, i );
        }

        RectanglePacker.Pack( rectangles, out var atlasBounds, PackingHints.MostlySquared );

        var atlas = new Image<Rgba32>( (int) atlasBounds.Width, (int) atlasBounds.Height );

        for ( int i = rectangles.Length - 1; i >= 0; i-- )
        {
            var rect = rectangles[i];
            var info = spritList[rect.Id];

            atlas.ProcessPixelRows( info.AtlasImage!, ( _atlas, _image ) =>
            {
                for ( int y = 0; y < rect.Height; y++ )
                {
                    Span<Rgba32> atlasRow = _atlas.GetRowSpan( y + (int) rect.Y );
                    Span<Rgba32> imageRow = _image.GetRowSpan( y );
                    for ( int x = 0; x < rect.Width; x++ )
                        atlasRow[x + (int) rect.X] = imageRow[x];
                }
            } );

            info.Rectangle.X = (int) rect.X;
            info.Rectangle.Y = (int) rect.Y;
        }

        return atlas;
    }

    private async Task UpdateImages()
    {
        foreach ( AtlasItem info in items.Values )
        {
            if ( info.AtlasImage != null )
                continue;

            var img = await MakeAtlasImage( info );
            info.AtlasImage = img.image;
            info.Rectangle = img.rect;
        }
    }

    async Task<AtlasMeta> LoadMeta()
    {
        if ( !metaFile.Exists )
            return new();

        // Loading data from existing sprite atlas
        await using FileStream file = metaFile.OpenRead();
        return JsonSerializer.Deserialize<AtlasMeta>( file ) ?? new();
    }

    async Task SaveMeta()
    {
        var meta = new AtlasMeta();
        meta.AtlasWidth = Width;
        meta.AtlasHeight = Height;
        meta.Items = items.ToDictionary( t => t.Key, t => t.Value.Rectangle );
        await using var jsonFile = File.Open( metaFile.FullName, FileMode.Create );
        await JsonSerializer.SerializeAsync( jsonFile, meta, new JsonSerializerOptions() { WriteIndented = true } );
    }

    void ApplyMeta( AtlasMeta _meta )
    {
        Width = _meta.AtlasWidth;
        Height = _meta.AtlasHeight;

        foreach ( var pair in _meta.Items )
        {
            if ( !items.TryGetValue( pair.Key, out var info ) )
                continue;

            var atlasRect = pair.Value;
            info.PreviousRectangle = atlasRect;
            info.Rectangle = (AtlasRectangle) atlasRect.Clone();
        }
    }

    async Task LoadImagesFromAtlas( AtlasMeta _meta )
    {
        if ( !imageFile.Exists )
            return;

        Image<Rgba32> atlas = await Image.LoadAsync<Rgba32>( imageFile.FullName );

        foreach ( var pair in _meta.Items )
        {
            if ( !items.TryGetValue( pair.Key, out var info ) )
                continue;

            if ( info.IsModified )
                continue;

            Debug.Assert( info.AtlasImage == null );

            var atlasRect = pair.Value;
            info.PreviousRectangle = atlasRect;
            info.Rectangle = (AtlasRectangle) atlasRect.Clone();
            info.AtlasImage = atlas.Clone( t => t.Crop( new Rectangle( atlasRect.X, atlasRect.Y, atlasRect.Width, atlasRect.Height ) ) );
        }
    }

    /// <summary>
    /// Generates image that will be place into the sprite atlas
    /// </summary>
    private async Task<(Image<Rgba32> image, AtlasRectangle rect)> MakeAtlasImage( AtlasItem _item )
    {
        AtlasRectangle rectangle = new AtlasRectangle();

        Image<Rgba32> spriteImage = await Image.LoadAsync<Rgba32>( _item.ImagePath.FullName );
        var trimRect = ImageUtilities.FindTrimRect( spriteImage );
        ImageUtilities.Trim( spriteImage, trimRect );

        var sprite = _item.Sprite;
        Vector2 relativePivot = GetSnappedPivot( sprite, sprite.xorigin - trimRect.Left, sprite.yorigin - trimRect.Top, trimRect.Width, trimRect.Height );

        int width, height;

        // Use small centered sprites as is
        if (_item.NoCropping)
        {
            width = sprite.width;
            height = sprite.height;
        }
        else
        if ( sprite.width <= cellSize && sprite.height <= cellSize && sprite.xorigin == sprite.width / 2 && sprite.yorigin == sprite.height / 2 )
        {
            width = cellSize;
            height = cellSize;
        }
        else
        {
            var left = CeilToGrid( sprite.xorigin - trimRect.Left );
            var right = CeilToGrid( trimRect.Left + trimRect.Width - sprite.xorigin );
            var top = CeilToGrid( sprite.yorigin - trimRect.Top );
            var bottom = CeilToGrid( trimRect.Height + trimRect.Top - sprite.yorigin );

            width = Math.Abs( relativePivot.X - 0.5f ) < 0.01f ? Math.Max( left, right ) * 2 : (relativePivot.X > 0.5f ? left : right);
            height = Math.Abs( relativePivot.Y - 0.5f ) < 0.01f ? Math.Max( top, bottom ) * 2 : (relativePivot.Y > 0.5f ? top : bottom);
        }


        if (!_item.NoCropping)
        {
            int maxSize = Math.Max(width, height);
            if (maxSize <= cellSize * 8)
            {
                width = maxSize;
                height = maxSize;
            }
        }

        int trimRectPivotX = (int) (width * relativePivot.X);
        int trimRectPivotY = (int) (height * relativePivot.Y);

        int putX = Math.Clamp( trimRectPivotX - sprite.xorigin + trimRect.Left, 0, width - 1 );
        int putY = Math.Clamp( trimRectPivotY - sprite.yorigin + trimRect.Top, 0, height - 1 );

        spriteImage.Mutate( t => t.Resize( new ResizeOptions()
        {
            Mode = ResizeMode.Manual,
            Position = AnchorPositionMode.Bottom,
            Size = new Size( width, height ),
            TargetRectangle = new Rectangle( putX, putY, spriteImage.Width, spriteImage.Height ),
        } ) );

        rectangle.Width = width;
        rectangle.Height = height;
        rectangle.EmptyLeft = Math.Max( 0, putX );
        rectangle.EmptyRight = Math.Max( 0, width - putX - trimRect.Width );
        rectangle.EmptyTop = Math.Max( 0, putY );
        rectangle.EmptyBottom = Math.Max( 0, height - putY - trimRect.Height );
        rectangle.PaddingLeft = putX - trimRect.Left;
        rectangle.PaddingTop = putY - trimRect.Top;
        rectangle.PaddingRight = width - putX - trimRect.Width - sprite.width + trimRect.Right;
        rectangle.PaddingBottom = height - putY - trimRect.Height - sprite.height + trimRect.Bottom;
        rectangle.PivotX = relativePivot.X;
        rectangle.PivotY = relativePivot.Y;

        return (spriteImage, rectangle);
    }

    private Vector2 GetSnappedPivot( GMSprite _sprite, int _trimPivotX, int _trimPivotY, int _trimWidth, int _trimHeight )
    {
        Vector2 relativePivot = default;

        if ( !SnapPivotPerfect( ref relativePivot.X, _sprite.xorigin, _sprite.width ) )
            SnapPivot( ref relativePivot.X, _trimPivotX, _trimWidth );

        if ( !SnapPivotPerfect( ref relativePivot.Y, _sprite.yorigin, _sprite.height ) )
            SnapPivot( ref relativePivot.Y, _trimPivotY, _trimHeight );

        return relativePivot;
    }

    bool SnapPivotPerfect( ref float _pivot, int _pivotReal, int _size )
    {
        if ( Math.Abs( _pivotReal - 1 ) <= 1 )
        {
            _pivot = 0f;
            return true;
        }

        if ( Math.Abs( _pivotReal - _size ) <= 1 )
        {
            _pivot = 1f;
            return true;
        }

        if ( Math.Abs( _pivotReal - _size / 2 ) <= 1 )
        {
            _pivot = 0.5f;
            return true;
        }

        return false;
    }

    void SnapPivot( ref float _pivot, int _pivotReal, int _size, float _threshold = 0.25f, float _cellThreshold = 0.375f )
    {
        float diffX = _pivotReal - _size / 2f;

        if ( Math.Abs( diffX ) > Math.Max( _size * _threshold, cellSize * _cellThreshold ) )
        {
            _pivot = diffX > 0 ? 1f : 0f;
        }
        else
        {
            _pivot = 0.5f;
        }
    }

    public int CeilToGrid( int _value )
    {
        return cellSize * (int) MathF.Ceiling( _value / (float) cellSize );
    }

    public int RoundToGrid( int _value )
    {
        return cellSize * Math.Max( 1, (int) MathF.Round( _value / (float) cellSize ) );
    }

    [System.Serializable]
    private class AtlasMeta
    {
        public int Version { get; set; } = 1;
        public int AtlasWidth { get; set; }
        public int AtlasHeight { get; set; }
        public Dictionary<string, AtlasRectangle> Items { get; set; } = new();
    }

    [System.Serializable]
    public class AtlasRectangle : IEquatable<AtlasRectangle>, ICloneable
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public float PivotX { get; set; }
        public float PivotY { get; set; }

        public int[] Padding { get; set; } = new int[4];
        public int[] Empty { get; set; } = new int[4];

        [JsonIgnore]
        public int Left => X;
        [JsonIgnore]
        public int Right => X + Width;
        [JsonIgnore]
        public int Top => Y;
        [JsonIgnore]
        public int Bottom => Y + Height;

        [JsonIgnore]
        public int PaddingLeft
        {
            get => Padding[0];
            set => Padding[0] = value;
        }

        [JsonIgnore]
        public int PaddingRight
        {
            get => Padding[1];
            set => Padding[1] = value;
        }

        [JsonIgnore]
        public int PaddingTop
        {
            get => Padding[2];
            set => Padding[2] = value;
        }

        [JsonIgnore]
        public int PaddingBottom
        {
            get => Padding[3];
            set => Padding[3] = value;
        }

        [JsonIgnore]
        public int EmptyLeft
        {
            get => Empty[0];
            set => Empty[0] = value;
        }

        [JsonIgnore]
        public int EmptyRight
        {
            get => Empty[1];
            set => Empty[1] = value;
        }

        [JsonIgnore]
        public int EmptyTop
        {
            get => Empty[2];
            set => Empty[2] = value;
        }

        [JsonIgnore]
        public int EmptyBottom
        {
            get => Empty[3];
            set => Empty[3] = value;
        }

        public AtlasRectangle SetFrom( PackingRectangle _source )
        {
            X = (int) _source.X;
            Y = (int) _source.Y;
            Width = (int) _source.Width;
            Height = (int) _source.Height;
            return this;
        }

        public bool Contains( Rectangle _rectangle )
        {
            return _rectangle.Left >= X && _rectangle.Top >= Y && _rectangle.Right <= X + Width && _rectangle.Bottom <= Y + Height;
        }

        public bool Equals( AtlasRectangle? other )
        {
            if ( ReferenceEquals( null, other ) ) return false;
            if ( ReferenceEquals( this, other ) ) return true;
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        public override bool Equals( object? obj )
        {
            if ( ReferenceEquals( null, obj ) ) return false;
            if ( ReferenceEquals( this, obj ) ) return true;
            if ( obj.GetType() != this.GetType() ) return false;
            return Equals( (AtlasRectangle) obj );
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add( X );
            hashCode.Add( Y );
            hashCode.Add( Width );
            hashCode.Add( Height );
            return hashCode.ToHashCode();
        }

        public override string ToString()
        {
            return $"{{x:{X}, y:{Y}, w:{Width}, h:{Height}}}";
        }

        public object Clone()
        {
            return new AtlasRectangle()
            {
                X = X,
                Y = Y,
                Width = Width,
                Height = Height,
                PivotX = PivotX,
                PivotY = PivotY,
                Padding = (int[]) Padding.Clone(),
                Empty = (int[]) Empty.Clone()
            };
        }
    }
}
