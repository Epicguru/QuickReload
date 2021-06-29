using QuickReload;
using Verse;

namespace TestAssembly
{
    public class MyClass
    {
        [DebugAction("Quick Reload", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.Entry)]
        [Reload]
        public static void PrintSomething()
        {
            Log.Message("Hello! This is some text! 9102390");

            DoSomething(2, 3, out int result);
            Log.Message(result.ToString());
        }

        [Reload]
        public static void DoSomething(int x, int y, out int result)
        {
            Log.Message($"x {x + 5} y {y}");
            result = x + y;
        }
    }
}
