using System;
using System.Reflection;

namespace QuickReload
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class ReloadAttribute : Attribute
    {
        internal MethodInfo Method;
    }
}
