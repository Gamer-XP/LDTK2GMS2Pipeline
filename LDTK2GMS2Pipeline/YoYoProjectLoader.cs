using ProjectManager.GameMaker;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProjectManager;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using YoYoStudio.Resources;

namespace LDTK2GMS2Pipeline;

public static class YoYoProjectLoader
{
    public static Task<GMProject> LoadGMProject()
    {
        FileInfo? gmProjectFile = GMSUtilities.FindProjectFileInParent();
        if (gmProjectFile == null)
            throw new FileNotFoundException("GameMaker project file not found");

        FileIO.SetDefaultFileFunctions();
        MessageIO.SetDefaultMessageFunctions();
        ResourceInfo.FindAllResources();
        GMProject.LicenseModules = new PlaceholderLicensingModule();

        var loadingWait = new TaskCompletionSource<GMProject>();

        ProjectInfo.LoadProject( gmProjectFile.FullName, true, ( _r ) =>
        {
            Console.WriteLine( $"Success: {_r.name}" );
            loadingWait.TrySetResult( (GMProject)_r );
        }, ( _r, _progress ) =>
        {
            Console.WriteLine( $"Loading: {Math.Round( _progress * 100 )}%" );
        }, ( _r ) =>
        {
            Console.WriteLine( $"Failure: {_r.name}" );
            throw new Exception("Failed to load GameMaker project");
        } );

        return loadingWait.Task;
    }

    class PlaceholderLicensingModule : ILicenseModulesSource
    {
        public IEnumerable<string> Modules { get; } = new List<string>();
    }

    public class GMSpriteWrapper : ISprite
    {
        public readonly GMSprite Sprite;

        public GMSpriteWrapper(GMSprite _sprite)
        {
            Sprite = _sprite;
        }

        public string Name
        {
            get => Sprite.name;
            set => Sprite.name = value;
        }

        public string Path
        {
            get => Sprite.path;
            set {}
        }

        public ResourceType Type => ResourceType.Sprite;

        public IFolder? Parent => null;

        public DateTime? GetLastModificationTime()
        {
            return null;
        }

        public int Width => Sprite.width;
        public int Height => Sprite.height;
        public int PivotX => Sprite.xorigin;
        public int PivotY => Sprite.yorigin;
        public int Count => Sprite.frames.Count;

        public Task<Image<Rgba32>> FrameLoad(int _index)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName( ProjectInfo.GetProjectPath( Sprite.project ) ), Sprite.GetCompositePaths()[_index]);
            return Image.LoadAsync<Rgba32>(path);
        }

        public void FrameSet(int _index, Image<Rgba32> _image)
        {
            throw new NotImplementedException();
        }

        public void FramesFree()
        {
            
        }

        public void Dispose()
        {
            
        }
    }
}
