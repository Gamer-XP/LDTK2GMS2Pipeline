using System.Dynamic;
using System.Reflection;
using Dynamitey.DynamicObjects;
using Microsoft.CSharp.RuntimeBinder;

namespace LDTK2GMS2Pipeline.Utilities;

public class DynamicInvoker : BaseForwarder
{
    public DynamicInvoker( object target ) : base(target) { }

    public override bool TryInvokeMember(InvokeMemberBinder binder, object[] args, out object result)
    {
        if (!base.TryInvokeMember(binder, args, out result))
        {
            try
            {
                object? target;
                Type type;
                BindingFlags flags = BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Public;
                
                if (CallTarget is Type tp)
                {
                    type = tp;
                    target = null;
                    flags |= BindingFlags.Static;
                }
                else
                {
                    type = CallTarget.GetType();
                    target = CallTarget;
                    flags |= BindingFlags.Instance;
                }

                MethodInfo? method = null;
                foreach (MethodInfo info in type.GetMethods(flags).Where( t => t.Name == binder.Name))
                {
                    if (info.GetParameters().Length != args.Length)
                        continue;
                    
                    method = info;
                    break;
                }

                if (method == null)
                    return false;
                
                object[] convertedArgs = new object[args.Length];
                var methodArgs = method.GetParameters();
                for (int i = methodArgs.Length - 1; i >= 0; i--)
                {
                    var arg = args[i];
                    
                    if (arg is IForwarder fwd)
                    {
                        arg = fwd.Target;
                    }
                    
                    if (arg is Delegate del)
                    {
                        convertedArgs[i] = Delegate.CreateDelegate(
                            methodArgs[i].ParameterType,
                            del.Target,
                            del.Method);
                    }
                    else
                    {
                        if (arg is IConvertible cnv)
                            convertedArgs[i] = cnv.ToType(methodArgs[i].ParameterType, null);
                        else
                            convertedArgs[i] = arg;
                    }   
                }

                result = method.Invoke(target, convertedArgs);
                return true;
            }
            catch (RuntimeBinderException ex)
            {
                return false;
            }
        }

        return false;
    }

    public override bool TrySetMember( SetMemberBinder binder, object? value )
    {
        if (value is Delegate del && FindMemberType(CallTarget.GetType(), binder.Name, out var type) && !type.IsInstanceOfType(del))
        {
            value = ConvertDelegate(del, type);
        }

        return base.TrySetMember(binder, value);
    }

    private bool CallDelegateDynamic( string _methodName, object[] args, out object? result )
    {
        result = default;
        object? target;
        Type type;
        BindingFlags flags = BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Public;
                
        if (CallTarget is Type tp)
        {
            type = tp;
            target = null;
            flags |= BindingFlags.Static;
        }
        else
        {
            type = CallTarget.GetType();
            target = CallTarget;
            flags |= BindingFlags.Instance;
        }

        MethodInfo? method = null;
        foreach (MethodInfo info in type.GetMethods(flags).Where( t => t.Name == _methodName))
        {
            if (info.GetParameters().Length != args.Length)
                continue;
                    
            method = info;
            break;
        }

        if (method == null)
            return false;
                
        object[] convertedArgs = new object[args.Length];
        ParameterInfo[] methodArgs = method.GetParameters();
        for (int i = methodArgs.Length - 1; i >= 0; i--)
        {
            if (args[i] is Delegate del)
                convertedArgs[i] = ConvertDelegate(del, methodArgs[i].ParameterType);
            else
                convertedArgs[i] = Convert.ChangeType(args[i], methodArgs[i].ParameterType);   
        }

        result = method.Invoke(target, convertedArgs);
        return true;
    }
    
    private Delegate ConvertDelegate( Delegate _delegate, Type _newType )
    {
        return Delegate.CreateDelegate(
            _newType,
            _delegate.Target,
            _delegate.Method);
    }
    
    private bool FindMemberType( Type _targetType, string _name, out Type _type )
    {
        foreach (MemberInfo info in _targetType.GetMember(_name, MemberTypes.Field | MemberTypes.Property, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            switch (info)
            {
                case FieldInfo fi:
                    _type = fi.FieldType;
                    return true;
                case PropertyInfo pi:
                    _type = pi.PropertyType;
                    return true;
            }
        }

        _type = null!;
        return false;
    }
}