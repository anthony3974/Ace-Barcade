using System.Collections.Generic;
using System.Linq;
using AceBarcade.Games.Noodle;
using NUnit.Framework;
using UnityEngine;
using Sim = AceBarcade.Games.Noodle.NoodleSim;

namespace AceBarcade.Tests
{
    public class NoodleSimTests
    {
        static readonly int[] Seeds = { 0, 1, 2, 3, 7, 42, 1234, 99999 };
        static readonly Vector2Int[] Dirs = { Sim.Up, Sim.Right, Sim.Down, Sim.Left };

        /// <summary>Exactly one crawl to the next cell.</summary>
        static NoodleEvent Crawl(Sim sim) => sim.Step(sim.StepTime);

        static NoodleEvent Crawl(Sim sim, int cells)
        {
            var all = NoodleEvent.None;
            for (int i = 0; i < cells && !sim.Crashed; i++) all |= Crawl(sim);
            return all;
        }

        static bool Blocked(Sim sim, Vector2Int c)
        {
            if (sim.Chilis.Contains(c)) return true;
            var body = sim.Body;
            int solid = sim.Growing ? body.Count : body.Count - 1; // the tail moves on, unless growing
            for (int i = 0; i < solid; i++) if (body[i] == c) return true;
            return false;
        }

        /// <summary>The first move of a shortest path from the head to the goal, or zero.</summary>
        static Vector2Int PathTo(Sim sim, Vector2Int goal)
        {
            var first = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            foreach (var d in Dirs)
            {
                if (d == -sim.Dir) continue;
                var c = Sim.Wrap(sim.Head + d);
                if (Blocked(sim, c) || first.ContainsKey(c)) continue;
                first[c] = d;
                queue.Enqueue(c);
            }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == goal) return first[c];
                foreach (var d in Dirs)
                {
                    var next = Sim.Wrap(c + d);
                    if (Blocked(sim, next) || first.ContainsKey(next)) continue;
                    first[next] = first[c];
                    queue.Enqueue(next);
                }
            }
            return Vector2Int.zero;
        }

        static int Room(Sim sim, Vector2Int from)
        {
            if (Blocked(sim, from)) return 0;
            var seen = new HashSet<Vector2Int> { from };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var d in Dirs)
                {
                    var next = Sim.Wrap(c + d);
                    if (!Blocked(sim, next) && seen.Add(next)) queue.Enqueue(next);
                }
            }
            return seen.Count;
        }

        /// <summary>A simple bot: the shortest way to the pea if it leaves room to move, else the roomiest way.</summary>
        static void BotTurn(Sim sim)
        {
            var go = PathTo(sim, sim.Pea);
            if (go == Vector2Int.zero || Room(sim, Sim.Wrap(sim.Head + go)) < sim.Body.Count + 4)
            {
                int best = -1;
                foreach (var d in Dirs)
                {
                    if (d == -sim.Dir) continue;
                    int room = Room(sim, Sim.Wrap(sim.Head + d));
                    if (room > best) { best = room; go = d; }
                }
            }
            sim.Turn(go);
        }

        /// <summary>The board as text, top row first, for failure messages.</summary>
        static string Picture(Sim sim)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"dir {sim.Dir}, crash at {sim.CrashCell}");
            for (int y = Sim.Rows - 1; y >= 0; y--)
            {
                for (int x = 0; x < Sim.Cols; x++)
                {
                    var c = new Vector2Int(x, y);
                    int i = sim.Body.ToList().IndexOf(c);
                    sb.Append(i == 0 ? 'H' : i > 0 ? (char)('a' + Mathf.Min(i - 1, 25)) : sim.Chilis.Contains(c) ? 'X' : sim.HasPea && sim.Pea == c ? 'o' : sim.HasGem && sim.Gem == c ? '*' : '.');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        // --- Start, crawling and wrapping ------------------------------------------------------

        [Test]
        public void Start_IsANoodleOfFour_HeadingUp_WithThePeaStraightAhead()
        {
            var sim = new Sim(1);
            Assert.AreEqual(Sim.StartLength, sim.Body.Count);
            Assert.AreEqual(Sim.StartHead, sim.Head);
            Assert.AreEqual(Sim.Up, sim.Dir);
            for (int i = 1; i < sim.Body.Count; i++) Assert.AreEqual(sim.Body[i - 1] - Sim.Up, sim.Body[i]);
            Assert.IsTrue(sim.HasPea);
            Assert.AreEqual(Sim.FirstPea, sim.Pea);
            Assert.AreEqual(Sim.StartHead.x, sim.Pea.x, "straight ahead");
            Assert.AreEqual(0, sim.Score);
            Assert.IsFalse(sim.HasGem);
            Assert.AreEqual(0, sim.Chilis.Count);
        }

        [Test]
        public void Crawls_OneCellPerStepTime_NotBefore()
        {
            var sim = new Sim(1);
            Assert.AreEqual(NoodleEvent.None, sim.Step(Sim.StartStep * 0.9f));
            Assert.AreEqual(Sim.StartHead, sim.Head);
            Assert.That(sim.Progress, Is.EqualTo(0.9f).Within(1e-4f));
            Assert.AreEqual(NoodleEvent.Moved, sim.Step(Sim.StartStep * 0.2f));
            Assert.AreEqual(Sim.StartHead + Sim.Up, sim.Head);
            Assert.AreEqual(Sim.StartLength, sim.Body.Count);
            Assert.AreEqual(Sim.StartHead, sim.Before[0], "Before is where each part was, for smooth drawing");
        }

        [Test]
        public void Wraps_RoundEveryEdge()
        {
            Assert.AreEqual(new Vector2Int(0, 3), Sim.Wrap(new Vector2Int(Sim.Cols, 3)));
            Assert.AreEqual(new Vector2Int(Sim.Cols - 1, 3), Sim.Wrap(new Vector2Int(-1, 3)));
            Assert.AreEqual(new Vector2Int(2, 0), Sim.Wrap(new Vector2Int(2, Sim.Rows)));
            Assert.AreEqual(new Vector2Int(2, Sim.Rows - 1), Sim.Wrap(new Vector2Int(2, -1)));
            Assert.AreEqual(2, Sim.Distance(new Vector2Int(0, 0), new Vector2Int(Sim.Cols - 1, Sim.Rows - 1)), "the short way round");

            // Crawl right all the way round, then up all the way round: back where it started.
            var sim = new Sim(1);
            Assert.IsTrue(sim.Turn(Sim.Right));
            Crawl(sim, Sim.Cols);
            Assert.AreEqual(Sim.StartHead, sim.Head);
            Assert.IsTrue(sim.Turn(Sim.Down));
            Crawl(sim, Sim.Rows);
            Assert.IsFalse(sim.Crashed);
            Assert.AreEqual(Sim.StartHead, sim.Head);
            foreach (var c in sim.Body)
            {
                Assert.That(c.x, Is.InRange(0, Sim.Cols - 1));
                Assert.That(c.y, Is.InRange(0, Sim.Rows - 1));
            }
        }

        // --- Turning ---------------------------------------------------------------------------

        [Test]
        public void Turns_IgnoreStraightBack_TheSameWay_AndMoreThanTwoWaiting()
        {
            var sim = new Sim(1);
            Assert.IsFalse(sim.Turn(Sim.Down), "straight back");
            Assert.IsFalse(sim.Turn(Sim.Up), "already going up");
            Assert.IsFalse(sim.Turn(new Vector2Int(1, 1)), "not a direction");
            Assert.IsTrue(sim.Turn(Sim.Left));
            Assert.IsFalse(sim.Turn(Sim.Left), "already turning left");
            Assert.IsFalse(sim.Turn(Sim.Right), "straight back from the queued turn");
            Assert.IsTrue(sim.Turn(Sim.Down));
            Assert.IsFalse(sim.Turn(Sim.Right), "the queue is full");
            Assert.AreEqual(Sim.MaxQueuedTurns, sim.QueuedTurns.Count);
            Assert.AreEqual(Sim.Up, sim.Dir, "turns wait for the next cell");
        }

        [Test]
        public void QuickTurns_TakeEffectOnTheNextCells_OneEach()
        {
            var sim = new Sim(1);
            sim.Turn(Sim.Left);
            sim.Turn(Sim.Down);
            Crawl(sim);
            Assert.AreEqual(Sim.Left, sim.Dir);
            Assert.AreEqual(Sim.StartHead + Sim.Left, sim.Head);
            Crawl(sim);
            Assert.AreEqual(Sim.Down, sim.Dir);
            Assert.AreEqual(Sim.StartHead + Sim.Left + Sim.Down, sim.Head);
            Assert.AreEqual(0, sim.QueuedTurns.Count);
            Assert.IsFalse(sim.Crashed, "a U-turn beside its own body is fine");
        }

        // --- Eating ----------------------------------------------------------------------------

        [Test]
        public void EatingAPea_Scores1_GrowsByOne_SpeedsUp_AndPutsANewPeaOnAFreeCell()
        {
            var sim = new Sim(5);
            int ahead = Sim.FirstPea.y - Sim.StartHead.y;
            var events = Crawl(sim, ahead);
            Assert.IsTrue((events & NoodleEvent.Ate) != 0);
            Assert.AreEqual(Sim.PeaPoints, sim.Score);
            Assert.AreEqual(1, sim.Peas);
            Assert.AreEqual(Sim.FirstPea, sim.EatenAt);
            Assert.Less(sim.StepTime, Sim.StartStep);
            Assert.IsTrue(sim.HasPea);
            Assert.AreNotEqual(Sim.FirstPea, sim.Pea);
            Assert.IsFalse(sim.Body.Contains(sim.Pea));

            Assert.AreEqual(Sim.StartLength, sim.Body.Count, "it grows as it crawls on");
            Crawl(sim);
            Assert.AreEqual(Sim.StartLength + 1, sim.Body.Count);
            Assert.AreEqual(sim.Body[sim.Body.Count - 1], sim.Before[sim.Body.Count - 1], "the new tail end stays put");
            Crawl(sim);
            Assert.AreEqual(Sim.StartLength + 1, sim.Body.Count);
        }

        [Test]
        public void ChasingItsOwnTail_IsSafe_ButBitingItsBody_EndsTheRun()
        {
            // A noodle of four going round a 2 x 2 square always moves into the cell its tail leaves.
            var sim = new Sim(1);
            var loop = new[] { Sim.Right, Sim.Down, Sim.Left, Sim.Up };
            for (int i = 0; i < 12; i++)
            {
                sim.Turn(loop[i % 4]);
                Crawl(sim);
            }
            Assert.IsFalse(sim.Crashed);

            // A noodle of six making the same tight turn bites itself.
            sim = new Sim(1, 6);
            foreach (var d in new[] { Sim.Right, Sim.Down, Sim.Left })
            {
                sim.Turn(d);
                Crawl(sim);
            }
            Assert.IsTrue(sim.Crashed);
            Assert.AreEqual(Sim.StartHead + Sim.Right + Sim.Down, sim.Head, "it stops before the bite");
            Assert.AreEqual(Sim.StartHead + Sim.Down, sim.CrashCell);
            int score = sim.Score;
            Assert.AreEqual(NoodleEvent.None, sim.Step(1f), "nothing moves after a crash");
            Assert.IsFalse(sim.Turn(Sim.Up));
            Assert.AreEqual(score, sim.Score);
        }

        // --- Whole runs ------------------------------------------------------------------------

        [Test]
        public void EveryFifthPea_BringsAGemAndAChili_GemsScore5AndGo_ChilisStayAndKeepTheirDistance()
        {
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                int gemsShown = 0, gemsGone = 0, chiliEvents = 0;
                for (int move = 0; move < 4000 && !sim.Crashed && sim.Peas < 30; move++)
                {
                    BotTurn(sim);
                    int peas = sim.Peas, gems = sim.Gems, score = sim.Score, chilis = sim.Chilis.Count;
                    var e = Crawl(sim);

                    if ((e & NoodleEvent.Ate) != 0)
                    {
                        Assert.AreEqual(score + Sim.PeaPoints, sim.Score);
                        Assert.AreEqual(sim.Peas % Sim.BonusEvery == 0, (e & NoodleEvent.Chili) != 0 || chilis == Sim.MaxChilis,
                            $"seed {seed}: a chili with every 5th pea");
                    }
                    if ((e & NoodleEvent.Gem) != 0)
                    {
                        Assert.AreEqual(score + Sim.GemPoints, sim.Score);
                        Assert.AreEqual(gems + 1, sim.Gems);
                    }
                    if ((e & NoodleEvent.GemShown) != 0)
                    {
                        gemsShown++;
                        Assert.AreEqual(0, sim.Peas % Sim.BonusEvery);
                        Assert.GreaterOrEqual(Sim.Distance(sim.Gem, sim.Head), Sim.GemMinDistance);
                        Assert.AreEqual(Sim.GemSteps, sim.GemStepsLeft);
                    }
                    if ((e & NoodleEvent.GemGone) != 0) { gemsGone++; Assert.IsFalse(sim.HasGem); }
                    if ((e & NoodleEvent.Chili) != 0)
                    {
                        chiliEvents++;
                        var newest = sim.Chilis[sim.Chilis.Count - 1];
                        Assert.GreaterOrEqual(Sim.Distance(newest, sim.Head), Sim.ChiliMinDistance, $"seed {seed}");
                    }
                    if (sim.Peas == peas && (e & NoodleEvent.Gem) == 0) Assert.AreEqual(score, sim.Score);

                    // Nothing overlaps, and no two chilis touch, even at a corner.
                    Assert.LessOrEqual(sim.Chilis.Count, Sim.MaxChilis);
                    for (int i = 0; i < sim.Chilis.Count; i++)
                    {
                        var c = sim.Chilis[i];
                        Assert.IsFalse(sim.Body.Contains(c));
                        Assert.IsFalse(sim.HasPea && sim.Pea == c);
                        Assert.IsFalse(sim.HasGem && sim.Gem == c);
                        for (int j = 0; j < i; j++)
                        {
                            int dx = Mathf.Abs(c.x - sim.Chilis[j].x), dy = Mathf.Abs(c.y - sim.Chilis[j].y);
                            Assert.IsTrue(Mathf.Min(dx, Sim.Cols - dx) > 1 || Mathf.Min(dy, Sim.Rows - dy) > 1, $"seed {seed}: chilis touch");
                        }
                    }
                    if (sim.HasPea) Assert.IsFalse(sim.Body.Contains(sim.Pea));
                }
                Assert.IsFalse(sim.Crashed, $"seed {seed}: the bot crashed at {sim.Peas} peas");
                Assert.AreEqual(30, sim.Peas, $"seed {seed}");
                Assert.AreEqual(30 / Sim.BonusEvery, chiliEvents);
                Assert.AreEqual(30 / Sim.BonusEvery, sim.Chilis.Count);
                Assert.GreaterOrEqual(gemsShown, 1);
                Assert.AreEqual(sim.Peas * Sim.PeaPoints + sim.Gems * Sim.GemPoints, sim.Score);
                Assert.AreEqual(Sim.StartLength + 29, sim.Body.Count, "one longer per pea (the last is still on its way down)");
            }
        }

        [Test]
        public void ASimpleBot_GetsPastTopSpeedAndAllEightChilis_OnEverySeed()
        {
            int topSpeedPeas = Mathf.CeilToInt((Sim.StartStep - Sim.MinStep) / Sim.StepFaster);
            int goal = Mathf.Max(topSpeedPeas, Sim.MaxChilis * Sim.BonusEvery) + 5;
            foreach (int seed in Seeds)
            {
                var sim = new Sim(seed);
                for (int move = 0; move < 20000 && !sim.Crashed && sim.Peas < goal; move++)
                {
                    BotTurn(sim);
                    Crawl(sim);
                }
                Assert.GreaterOrEqual(sim.Peas, goal, $"seed {seed}: crashed at {sim.Peas} peas\n{Picture(sim)}");
                Assert.AreEqual(Sim.MinStep, sim.StepTime, 1e-6f);
                Assert.AreEqual(Sim.MaxChilis, sim.Chilis.Count);
            }
        }

        [Test]
        public void CrawlingIntoAChili_EndsTheRun()
        {
            var sim = new Sim(3);
            while (sim.Chilis.Count == 0 && !sim.Crashed) { BotTurn(sim); Crawl(sim); }
            Assert.IsFalse(sim.Crashed);
            var chili = sim.Chilis[0];
            for (int move = 0; move < 200 && !sim.Crashed; move++)
            {
                // Head for the chili: the bot's path search treats it as a wall, so aim at a free
                // neighbour first, then step in.
                foreach (var d in Dirs)
                    if (Sim.Wrap(sim.Head + d) == chili && d != -sim.Dir) { sim.Turn(d); break; }
                if (sim.QueuedTurns.Count == 0 && Sim.Wrap(sim.Head + sim.Dir) != chili)
                {
                    foreach (var d in Dirs)
                    {
                        var go = PathTo(sim, Sim.Wrap(chili + d));
                        if (go != Vector2Int.zero) { sim.Turn(go); break; }
                    }
                }
                Crawl(sim);
            }
            Assert.IsTrue(sim.Crashed);
            Assert.AreEqual(chili, sim.CrashCell);
        }

        // --- Steering --------------------------------------------------------------------------

        [Test]
        public void Steer_TurnsEachTimeTheFingerMovesFarEnough_EvenWithoutLifting()
        {
            var steer = new NoodleSteer();
            var p = new Vector2(0f, -3f);
            Assert.AreEqual(Vector2Int.zero, steer.Feed(true, p), "touching down isn't a turn");
            Assert.AreEqual(Vector2Int.zero, steer.Feed(true, p + new Vector2(0.3f, 0.1f)), "a little wobble isn't");
            Assert.AreEqual(Vector2Int.right, steer.Feed(true, p + new Vector2(0.7f, 0.2f)));
            p += new Vector2(0.7f, 0.2f);
            Assert.AreEqual(Vector2Int.zero, steer.Feed(true, p + new Vector2(0.2f, 0f)));
            Assert.AreEqual(Vector2Int.up, steer.Feed(true, p + new Vector2(0.2f, 0.7f)), "then up, in the same stroke");
            Assert.AreEqual(Vector2Int.zero, steer.Feed(false, p), "lifting isn't a turn");

            // A tap: no turn. A new touch starts from where it lands.
            Assert.AreEqual(Vector2Int.zero, steer.Feed(true, new Vector2(3f, 3f)));
            Assert.AreEqual(Vector2Int.zero, steer.Feed(false, new Vector2(3f, 3f)));
            Assert.AreEqual(Vector2Int.zero, steer.Feed(true, new Vector2(-3f, -3f)), "jumping to a new spot isn't a turn");
            Assert.AreEqual(Vector2Int.down, steer.Feed(true, new Vector2(-3f, -3.8f)));
            Assert.AreEqual(Vector2Int.left, steer.Feed(true, new Vector2(-3.8f, -3.8f)));
        }
    }
}
