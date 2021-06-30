using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace QuickReload
{
    public class Core : Mod
    {
        public static Harmony Harmony { get; private set; }

        private static HashSet<string> watchedDirectories = new HashSet<string>();
        private static List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private static Dictionary<(Mod mod, Assembly ass), List<ReloadAttribute>> methods = new Dictionary<(Mod mod, Assembly ass), List<ReloadAttribute>>();
        private static Dictionary<MethodInfo, MethodInfo> patches = new Dictionary<MethodInfo, MethodInfo>();

        public static void Log(string message)
        {
            Verse.Log.Message($"<color=#b67aff>[QuickReload]</color> {message ?? "<null>"}");
        }

        public static void Error(string message, Exception e = null)
        {
            Verse.Log.Error($"<color=#b67aff>[QuickReload]</color> {message ?? "<null>"}");
            if (e != null)
                Verse.Log.Error(e.ToString());
        }

        public Core(ModContentPack content) : base(content)
        {
            Harmony = new Harmony("co.uk.epicguru.quickreload");
            Log("Hello, world!");

            LongEventHandler.QueueLongEvent(Load, "Preparing for Quick Reload...", false, null);
        }

        public void Load()
        {
            const BindingFlags FLAGS = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            int methodCount = 0;
            int watcherCount = 0;
            methods.Clear();
            HashSet<ModContentPack> seenContents = new HashSet<ModContentPack>();
            foreach (var mod in LoadedModManager.ModHandles)
            {
                if (mod?.Content?.assemblies?.loadedAssemblies == null)
                    continue;

                if (seenContents.Contains(mod.Content))
                {
                    Log($"Skipping {mod} since content has already been visited.");
                    continue;
                }

                seenContents.Add(mod.Content);

                bool hasInMod = false;

                foreach (var ass in mod.Content.assemblies.loadedAssemblies)
                {
                    foreach (var type in ass.GetTypes())
                    {
                        foreach (var method in type.GetMethods(FLAGS))
                        {
                            var attr = method.GetCustomAttribute<ReloadAttribute>();
                            if (attr == null)
                                continue;

                            attr.Method = method;

                            if (!methods.TryGetValue((mod, ass), out var list))
                            {
                                list = new List<ReloadAttribute>();
                                methods.Add((mod, ass), list);
                            }
                            list.Add(attr);
                            methodCount++;

                            if (hasInMod)
                                continue;

                            hasInMod = true;

                            foreach (var pair in ModContentPack.GetAllFilesForModPreserveOrder(mod.Content, "Assemblies/", e => e.ToLower() == ".dll"))
                            {
                                string folder = pair.Item2.DirectoryName;
                                if (watchedDirectories.Contains(folder))
                                    continue;

                                watchedDirectories.Add(folder);
                                var watcher = MakeWatcher(folder, mod, ass);
                                watchers.Add(watcher);
                                watcherCount++;
                            }
                        }
                    }
                }
            }

            Log($"There are {methods.Count} mods with a total of {methodCount} reloading methods. There are {watcherCount} watchers:\n{string.Join(", ", methods.Keys)}");
        }

        private FileSystemWatcher MakeWatcher(string folder, Mod mod, Assembly ass)
        {
            var watcher = new FileSystemWatcher(folder);

            watcher.NotifyFilter = NotifyFilters.Attributes
                                   | NotifyFilters.CreationTime
                                   | NotifyFilters.DirectoryName
                                   | NotifyFilters.FileName
                                   | NotifyFilters.LastAccess
                                   | NotifyFilters.LastWrite
                                   | NotifyFilters.Security
                                   | NotifyFilters.Size;

            watcher.Created += (_, args) =>
            {
                try
                {
                    OnAssemblyChanged(mod, ass, args.FullPath);
                }
                catch(Exception e)
                {
                    Error($"Exception reloading assembly '{args.Name}' for mod {mod.Content.Name}. Exception:", e);
                }
            };

            watcher.Filter = "*.dll";
            watcher.IncludeSubdirectories = false;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void OnAssemblyChanged(Mod mod, Assembly ass, string newFile)
        {
            // Find all methods that need patching.

            var newAss = Assembly.Load(File.ReadAllBytes(newFile));
            string simpleName = newAss.GetName().Name;
            if (simpleName.IndexOf('_') < 0)
                return;
            simpleName = simpleName.Substring(0, simpleName.LastIndexOf('_'));

            string fileAssName = new FileInfo(newFile).Name;
            if (fileAssName.IndexOf('_') < 0)
                return;

            fileAssName = fileAssName.Substring(0, fileAssName.LastIndexOf('_'));

            if (simpleName != fileAssName)
            {
                Core.Error($"{simpleName} != {fileAssName}");
            }

            foreach (var attr in methods[(mod, ass)])
            {
                blocker ??= GetType().GetMethod("Blocker", BindingFlags.NonPublic | BindingFlags.Static);
                var existingPatch = patches.TryGetValue(attr.Method);
                if (existingPatch != null)
                {
                    //Log($"Unpatching '{attr.Method.Name}'...");
                    Harmony.Unpatch(attr.Method, existingPatch);
                    Harmony.Unpatch(attr.Method, blocker);
                }

                var replacement = GetEquivalentMethod(newAss, attr.Method);
                if(replacement == null)
                {
                    Error($"Failed to find replacement method '{attr.Method.DeclaringType.FullName}.{attr.Method.Name}'. Has it been removed?");
                    continue;
                }

                Log($"Reloading '{attr.Method.Name}'...");

                Harmony.Patch(attr.Method, new HarmonyMethod(replacement) { priority = Priority.First });
                Harmony.Patch(attr.Method, new HarmonyMethod(blocker) { priority = Priority.First - 1 });

                patches[attr.Method] = replacement;
            }

            Messages.Message($"Reloaded {methods[(mod, ass)].Count} methods for {mod.Content.Name}", MessageTypeDefOf.PositiveEvent, false);
        }

        private MethodInfo GetEquivalentMethod(Assembly ass, MethodInfo original)
        {
            Type type = ass.GetType(original.DeclaringType.FullName, true, false);

            var argsTemp = original.GetParameters();
            Type[] args = new Type[argsTemp.Length];
            for (int i = 0; i < args.Length; i++)
                args[i] = argsTemp[i].ParameterType;
            Type[] generics = original.IsGenericMethod ? original.GetGenericArguments() : null;

            var method = AccessTools.DeclaredMethod(type, original.Name);
            return method;
        }

        private static MethodInfo blocker;
        private static bool Blocker()
        {
            return false;
        }
    }
}
