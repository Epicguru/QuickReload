using System.Runtime.CompilerServices;
using System;
using System.Reflection;

[assembly: InternalsVisibleTo("ModCore")]

namespace QuickReload
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ReloadAttribute : Attribute
    {
        internal MethodInfo Method;
    }
}
