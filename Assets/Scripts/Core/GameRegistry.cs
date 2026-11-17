using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AceBarcade
{
    /// <summary>One registered game: its metadata and the class that runs it.</summary>
    public sealed class GameInfo
    {
        public ArcadeGameAttribute Meta { get; }
        public Type Type { get; }

        public int Number => Meta.Number;
        public string Id => Meta.Id;
        public string Title => Meta.Title;

        /// <summary>The folder name used under Assets/Games, Art/Games and Sprites/Games, e.g. G001_PaddleRally.</summary>
        public string Folder => $"G{Number:000}_{Regex.Replace(Title, "[^A-Za-z0-9]", "")}";

        public GameInfo(ArcadeGameAttribute meta, Type type)
        {
            Meta = meta;
            Type = type;
        }
    }

    /// <summary>
    /// Finds every <see cref="ArcadeGame"/> marked with <see cref="ArcadeGameAttribute"/>.
    /// The games assembly is kept whole in Android builds by Assets/link.xml, so nothing found
    /// here is stripped.
    /// </summary>
    public static class GameRegistry
    {
        static List<GameInfo> _all;

        /// <summary>Every game, in number order.</summary>
        public static IReadOnlyList<GameInfo> All => _all ??= Scan(AppDomain.CurrentDomain.GetAssemblies());

        public static GameInfo Find(string id) => All.FirstOrDefault(g => g.Id == id);

        public static List<GameInfo> Scan(IEnumerable<System.Reflection.Assembly> assemblies)
        {
            var found = new List<GameInfo>();
            foreach (var asm in assemblies)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                foreach (var t in types)
                {
                    if (t.IsAbstract || !typeof(ArcadeGame).IsAssignableFrom(t)) continue;
                    var meta = (ArcadeGameAttribute)Attribute.GetCustomAttribute(t, typeof(ArcadeGameAttribute));
                    if (meta != null) found.Add(new GameInfo(meta, t));
                }
            }
            return found.OrderBy(g => g.Number).ToList();
        }

        static readonly Regex IdPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");

        /// <summary>Problems with a set of games (bad ids, repeated numbers or ids). Empty when all is well.</summary>
        public static List<string> Validate(IEnumerable<GameInfo> games)
        {
            var problems = new List<string>();
            var list = games.ToList();
            foreach (var g in list)
            {
                if (g.Number < 1 || g.Number > 999) problems.Add($"{g.Type.Name}: number {g.Number} is outside 1-999");
                if (g.Id == null || !IdPattern.IsMatch(g.Id)) problems.Add($"{g.Type.Name}: id '{g.Id}' must be lower-case words with dashes");
                if (string.IsNullOrWhiteSpace(g.Title)) problems.Add($"{g.Type.Name}: has no title");
            }
            foreach (var dup in list.GroupBy(g => g.Number).Where(x => x.Count() > 1))
                problems.Add($"number {dup.Key} is used by {string.Join(", ", dup.Select(g => g.Type.Name))}");
            foreach (var dup in list.GroupBy(g => g.Id).Where(x => x.Count() > 1))
                problems.Add($"id '{dup.Key}' is used by {string.Join(", ", dup.Select(g => g.Type.Name))}");
            return problems;
        }
    }
}
