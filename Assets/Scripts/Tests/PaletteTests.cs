using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class PaletteTests
    {
        static (string Name, string Hex)[] ReadPaletteFile()
        {
            var path = Path.Combine(Application.dataPath, "..", "Art", "palette.txt");
            return File.ReadAllLines(path)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => l.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries))
                .Select(p => (p[0], p[1].ToUpperInvariant()))
                .ToArray();
        }

        [Test]
        public void PaletteCs_MatchesArtPaletteFile()
        {
            var file = ReadPaletteFile();
            Assert.AreEqual(file.Length, Palette.All.Length, "Art/palette.txt and Palette.cs have a different number of colors");
            for (int i = 0; i < file.Length; i++)
            {
                Assert.AreEqual(file[i].Name, Palette.All[i].Name, $"color {i} is named differently");
                Assert.AreEqual(file[i].Hex, "#" + ColorUtility.ToHtmlStringRGB(Palette.All[i].Color), $"{file[i].Name} has a different value");
            }
        }

        [Test]
        public void PaletteNames_AreUnique()
        {
            var names = Palette.All.Select(c => c.Name).ToList();
            CollectionAssert.AllItemsAreUnique(names);
        }
    }
}
