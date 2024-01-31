using LDTK2GMS2Pipeline.LDTK;
using Spectre.Console;

namespace LDTK2GMS2Pipeline.Utilities;

/// <summary>
/// Simplified console wrapper to draw things in tree-like structure
/// </summary>
public static class Log
{
    public const string ColorWarning = "yellow";
    public const string ColorError = "red";
    
    public const string ColorDeleted = "navy";
    public const string ColorCreated = "green";
    
    public const string ColorEntity = "teal";
    public const string ColorLevel = "olive";
    public const string ColorLayer = "lime";
    public const string ColorField = "purple";
    public const string ColorAsset = "teal";
    public const string ColorMisc = "fuchsia";
    
    public static void Write( FormattableString _text )
    {
        WriteStack();
        WriteLinePadded(_text, level);
    }
    
    public static void Push( FormattableString _text, bool _optional = true )
    {
        PushAction( (i) => WriteLinePadded(_text, i), _optional);
    }

    public static void Push()
    {
        PushAction((i) => {});
    }

    public static void PushResource( LDTKProject.IResource _resource )
    {
        Push($"{_resource.GetType().Name} [{GetColor(_resource.GetType())}]{_resource.identifier}[/]");
    }

    public static void Pop()
    {
        if (treeStack.Count > 0)
            treeStack.RemoveAt(treeStack.Count - 1);
        
        level--;
    }

    public static void PushTitle( string _title )
    {
        level--;
        PushAction((i) =>
        {
            AnsiConsole.Write( new Rule(_title) );
        }, true);
    }

    public static void PopTitle()
    {
        Pop();
        level++;
    }

    public static IDisposable PopOnDispose()
    {
        return new TreeStacker();
    }
    
    public enum AutoLogLevel
    {
        Off,
        On,
        Auto,
    }

    private static AutoLogLevel EnableAutoLog = AutoLogLevel.Auto;
    
    public static bool CanAutoLog( Type _type )
    {
        switch ( EnableAutoLog )
        {
            case AutoLogLevel.Off:
                return false;
            case AutoLogLevel.On:
                return true;
            default:
                // EntityInstances are not logged automatically because you also want to log entity's name as well
                return _type != typeof( LDTKProject.Level.EntityInstance );
        }
    }

    private static Dictionary<Type, string> colorCodes = new()
    {
        { typeof(LDTKProject.Field), ColorField },
        { typeof(LDTKProject.Level.FieldInstance), ColorField },
        { typeof(LDTKProject.Layer), ColorLayer },
        { typeof(LDTKProject.Level.Layer), ColorLayer },
        { typeof(LDTKProject.Level), ColorLevel },
        { typeof(LDTKProject.Entity), ColorEntity }
    };

    public static string GetColor( Type _type )
    {
        return colorCodes.GetValueOrDefault(_type, ColorAsset);
    }
    
    private static int level;

    private static int treeStackRootLevel;
    private static List<Action<int>> treeStack = new();
    
    public class TreeStacker : IDisposable
    {
        public void Dispose()
        {
            Pop();
        }
    }

    private static void PushAction( Action<int> _act, bool _optional = true )
    {
        if (treeStack.Count == 0) 
            treeStackRootLevel = level;
        
        treeStack.Add(_act);
        level++;

        if (!_optional)
            WriteStack();
    }

    private static void WriteLinePadded( FormattableString _text, int _level )
    {
        if (_level > 0)
        {
            IEnumerable<char> GeneratePadding()
            {
                for (int i = _level - 1; i > 0; i--)
                {
                    yield return ' ';
                    yield return ' ';
                }
                
                yield return '-';
                yield return ' ';
            }
            AnsiConsole.Write($"{string.Concat(GeneratePadding())}");
        }
        
        AnsiConsole.MarkupLineInterpolated(_text);
    }

    private static void WriteStack()
    {
        for (int i = 0; i < treeStack.Count; i++)
        {
            var elem = treeStack[i];
            elem( i + treeStackRootLevel);
        }
        treeStack.Clear();
        treeStackRootLevel = level;
    }

    private static void Test()
    {
        Write($"Foo" );
        Write($"Bar" );
        
        Push($"A" );
        {
            Push($"AA");
            {
                Write($"AAA");
                Write($"AAB");
                Write($"AAC");
            }
            Pop();
            
            Push($"AB");
            {
                Push($"ABA");
                {
                    
                }
                Pop();
                
                Push($"ABB");
                {
                    Write($"ABBA");
                }
                Pop();
                
                Push($"ABC");
                {
                    
                }
                Pop();
            }
            Pop();
            
            Push($"AC");
            {
                Push($"ACA");
                {
                    Push($"ACAA");
                    {
                        Write($"ACAAA");
                    }
                    Pop();
                }
                Pop();
            }
            Pop();
            
            Write($"AD");
        }
        Pop();

        Write($"END" );
    }
}