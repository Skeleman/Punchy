using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Punchy.Tests
{
    /// <summary>
    /// A-134 row 3: the thieves' record rides in the quest save. A save written before thefts existed reads as no
    /// record, and a record survives a write and read unchanged (faded ones dropped). Reached by reflection, as
    /// UnitLibraryTests does, since the test assembly can't reference Assembly-CSharp.
    /// </summary>
    public class TheftSaveTests
    {
        static Assembly game;
        static Assembly Game => game ??= AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "Assembly-CSharp");
        static Type SaveFile => Game.GetType("Punchy.Items.Items+QuestSaveFile");
        static Type Thefts => Game.GetType("Punchy.Fauna.Thefts");
        static Type SavedTheft => Game.GetType("Punchy.Fauna.SavedTheft");
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        static void Load(Array saved) => Thefts.GetMethod("Load", Any).Invoke(null, new object[] { saved });
        static Array Save() => (Array)Thefts.GetMethod("Save", Any).Invoke(null, null);
        static int Held => ((ICollection)Thefts.GetField("Mine", Any).GetValue(null)).Count;
        static object Get(object o, string field) => o.GetType().GetField(field, Any).GetValue(o);

        static Array Saved(params (string house, string item, int ago)[] thefts)
        {
            var array = Array.CreateInstance(SavedTheft, thefts.Length);
            for (int i = 0; i < thefts.Length; i++)
            {
                var t = Activator.CreateInstance(SavedTheft);
                SavedTheft.GetField("house").SetValue(t, thefts[i].house);
                SavedTheft.GetField("item").SetValue(t, thefts[i].item);
                SavedTheft.GetField("ago").SetValue(t, thefts[i].ago);
                array.SetValue(t, i);
            }
            return array;
        }

        [TearDown]
        public void Clear() => Load(null);

        [Test]
        public void AnOldSaveWithoutTheftsReadsAsNone()
        {
            Load(Saved(("Hut@1.0,2.0", "Fish", 0)));
            var file = JsonUtility.FromJson("{\"world\":\"X\",\"quests\":[]}", SaveFile);
            Assert.IsNull(Get(file, "thefts"), "an old save has no thefts field");
            Load((Array)Get(file, "thefts"));
            Assert.AreEqual(0, Held, "an old save leaves no record");
            Assert.AreEqual(0, Save().Length);
        }

        [Test]
        public void TheRecordSurvivesAWriteAndRead()
        {
            var fade = (int)Thefts.GetField("FadeDays", Any).GetValue(null);
            Load(Saved(("Hut@1.0,2.0", "Fish", 0), ("Longhouse@-3.5,40.0", "Bread", fade - 1), ("Hut@9.0,9.0", "Rope", fade)));
            Assert.AreEqual(2, Held, "a theft FadeDays old or more is forgotten");
            var file = Activator.CreateInstance(SaveFile);
            SaveFile.GetField("world", Any).SetValue(file, "X");
            SaveFile.GetField("thefts", Any).SetValue(file, Save());
            var read = JsonUtility.FromJson(JsonUtility.ToJson(file), SaveFile);
            var thefts = (Array)Get(read, "thefts");
            Assert.AreEqual(2, thefts.Length);
            Assert.AreEqual("Longhouse@-3.5,40.0", Get(thefts.GetValue(1), "house"));
            Assert.AreEqual("Bread", Get(thefts.GetValue(1), "item"));
            Assert.AreEqual(fade - 1, Get(thefts.GetValue(1), "ago"));
            Load(thefts);
            Assert.AreEqual(2, Held);
        }
    }
}
