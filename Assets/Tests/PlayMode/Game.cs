using System;
using System.Linq;
using System.Reflection;

namespace Punchy.Tests
{
    /// <summary>
    /// The tests' one door into the game (W-97). A test assembly can't reference Assembly-CSharp, and giving all game
    /// code asmdefs is out of scope, so the tests call the game's public statics by reflection: Punchy.Debugging.TestHooks
    /// (plain values back) and DebugControls.RunOptions (the test flags, mid-session). Keep this class thin: put typed
    /// measuring code in TestHooks, game side.
    /// </summary>
    public static class Game
    {
        static Assembly game;

        static Assembly GameAssembly => game ??= AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");

        static MethodInfo Method(string type, string name) =>
            GameAssembly.GetType(type, true).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(type, name);

        /// <summary>Calls a public static on TestHooks.</summary>
        public static T Hook<T>(string name, params object[] args) => (T)Method("Punchy.Debugging.TestHooks", name).Invoke(null, args);

        /// <summary>Reads a public static field on TestHooks.</summary>
        public static T HookField<T>(string name) =>
            (T)GameAssembly.GetType("Punchy.Debugging.TestHooks", true).GetField(name, BindingFlags.Public | BindingFlags.Static).GetValue(null);

        /// <summary>Test flags applied on the next frame, as Temp/PlayArgs.txt applies them at the start ("-weather Rain").</summary>
        public static void RunOptions(string line) => Method("Punchy.Debugging.DebugControls", "RunOptions").Invoke(null, new object[] { line });
    }
}
