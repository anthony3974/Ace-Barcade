using System.Linq;
using AceBarcade.Games.DropStack;
using NUnit.Framework;
using UnityEngine;

namespace AceBarcade.Tests
{
    public class DropSimTests
    {
        static void FillRow(DropSim sim, int row, int except = -1, int value = 1)
        {
            for (int x = 0; x < DropSim.Cols; x++) sim.Cells[x, row] = x == except ? 0 : value;
        }

        [Test]
        public void NewPiece_AppearsAtTheTopMiddle_InsideTheWell()
        {
            var sim = new DropSim(1);
            foreach (var c in sim.PieceCells())
            {
                Assert.That(c.x, Is.InRange(0, DropSim.Cols - 1));
                Assert.That(c.y, Is.InRange(DropSim.Rows - 3, DropSim.Rows - 1));
            }
            Assert.IsFalse(sim.Lost);
        }

        [Test]
        public void Walls_StopSideways_Moves()
        {
            var sim = new DropSim();
            sim.SetPiece(PieceKind.Square, new Vector2Int(3, 5));
            int moves = 0;
            while (sim.Move(1)) moves++;
            Assert.AreEqual(DropSim.Cols - 2, sim.Position.x, "a 2-wide square stops at the right wall");
            Assert.AreEqual(3, moves);
            while (sim.Move(-1)) { }
            Assert.AreEqual(0, sim.Position.x);
        }

        [Test]
        public void Rotate_TurnsClockwise_AndKicksOffAWall()
        {
            var sim = new DropSim();
            sim.SetPiece(PieceKind.Bar, new Vector2Int(3, 5));
            Assert.IsTrue(sim.Rotate());
            CollectionAssert.AreEquivalent(new[] { new Vector2Int(3, 6), new Vector2Int(3, 5), new Vector2Int(3, 4) }, sim.PieceCells(), "a flat bar stands up");

            // Standing against the left wall, turning flat would poke through it: it slides right.
            sim.SetPiece(PieceKind.Bar, new Vector2Int(0, 5), rotation: 1);
            Assert.IsTrue(sim.Rotate());
            Assert.IsTrue(sim.PieceCells().All(c => c.x >= 0));
        }

        [Test]
        public void Square_DoesNotChangeWhenTurned()
        {
            var sim = new DropSim();
            sim.SetPiece(PieceKind.Square, new Vector2Int(3, 5));
            var before = sim.PieceCells();
            sim.Rotate();
            CollectionAssert.AreEquivalent(before, sim.PieceCells());
        }

        [Test]
        public void Gravity_DropsAPieceARowPerInterval_ThenItLocks()
        {
            var sim = new DropSim();
            sim.SetPiece(PieceKind.Square, new Vector2Int(3, 3));
            sim.Step(sim.FallInterval + 0.001f);
            Assert.AreEqual(2, sim.Position.y);

            // Let it reach the floor and sit there past the lock delay.
            DropEvent all = DropEvent.None;
            for (int i = 0; i < 200 && (all & DropEvent.Locked) == 0; i++) all |= sim.Step(0.05f);
            Assert.IsTrue((all & DropEvent.Locked) != 0);
            Assert.AreNotEqual(0, sim.Cells[3, 0]);
            Assert.AreNotEqual(0, sim.Cells[4, 1]);
        }

        [Test]
        public void HardDrop_LandsOnTheStack()
        {
            var sim = new DropSim();
            FillRow(sim, 0, except: 0);
            sim.SetPiece(PieceKind.Square, new Vector2Int(5, 10));
            var e = sim.HardDrop();
            Assert.IsTrue((e & DropEvent.Locked) != 0);
            Assert.AreNotEqual(0, sim.Cells[5, 1]);
            Assert.AreNotEqual(0, sim.Cells[6, 2]);
        }

        [Test]
        public void FullRow_Clears_AndTheRowsAboveDrop()
        {
            var sim = new DropSim();
            FillRow(sim, 0, except: 7);
            sim.Cells[2, 1] = 3; // something sitting on the row
            sim.SetPiece(PieceKind.Bar, new Vector2Int(7, 6), rotation: 1); // a standing bar in the gap
            var e = sim.HardDrop();

            Assert.IsTrue((e & DropEvent.Cleared) != 0);
            CollectionAssert.AreEqual(new[] { 0 }, sim.LastCleared);
            Assert.AreEqual(1, sim.Lines);
            Assert.AreEqual(DropSim.LinePoints[1], sim.Score);
            Assert.AreEqual(3, sim.Cells[2, 0], "the block above dropped into the cleared row");
            Assert.AreNotEqual(0, sim.Cells[7, 0], "the rest of the bar dropped too");
            Assert.AreNotEqual(0, sim.Cells[7, 1]);
            Assert.AreEqual(0, sim.Cells[7, 2]);
        }

        [Test]
        public void ThreeRowsAtOnce_ScoreMoreThanThreeSingles()
        {
            var sim = new DropSim();
            for (int y = 0; y < 3; y++) FillRow(sim, y, except: 0);
            sim.SetPiece(PieceKind.Bar, new Vector2Int(0, 8), rotation: 1);
            sim.HardDrop();
            Assert.AreEqual(3, sim.Lines);
            Assert.AreEqual(DropSim.LinePoints[3], sim.Score);
            Assert.Greater(DropSim.LinePoints[3], DropSim.LinePoints[1] * 3);
        }

        [Test]
        public void GemInAClearedRow_PaysTheBonus()
        {
            var sim = new DropSim();
            FillRow(sim, 0, except: 7);
            sim.Cells[3, 0] = DropSim.GemCell;
            sim.SetPiece(PieceKind.Bar, new Vector2Int(7, 6), rotation: 1);
            var e = sim.HardDrop();
            Assert.IsTrue((e & DropEvent.Gem) != 0);
            Assert.AreEqual(DropSim.LinePoints[1] + DropSim.GemPoints, sim.Score);
        }

        [Test]
        public void PieceWithAGem_LocksItAsAGemBlock()
        {
            var sim = new DropSim();
            sim.SetPiece(PieceKind.Square, new Vector2Int(0, 6), gemIndex: 0);
            sim.HardDrop();
            Assert.AreEqual(DropSim.GemCell, sim.Cells[0, 0]);
            Assert.AreEqual(DropSim.ColorOf(PieceKind.Square), sim.Cells[1, 0]);
        }

        [Test]
        public void Levels_SpeedUpTheFall()
        {
            var sim = new DropSim();
            float start = sim.FallInterval;
            for (int i = 0; i < DropSim.LinesPerLevel; i++)
            {
                FillRow(sim, 0, except: 0);
                sim.SetPiece(PieceKind.Bar, new Vector2Int(0, 8), rotation: 1);
                sim.HardDrop();
                for (int y = 0; y < DropSim.Rows; y++) for (int x = 0; x < DropSim.Cols; x++) sim.Cells[x, y] = 0;
            }
            Assert.AreEqual(1, sim.Level);
            Assert.Less(sim.FallInterval, start);
        }

        [Test]
        public void NoRoomForTheNextPiece_EndsTheGame()
        {
            var sim = new DropSim();
            // Block the spot where every new piece appears (all shapes include their centre block).
            sim.Cells[DropSim.Cols / 2 - 1, DropSim.Rows - 2] = 1;
            sim.SetPiece(PieceKind.Square, new Vector2Int(0, 3));
            var e = sim.HardDrop();
            Assert.IsTrue((e & DropEvent.Lost) != 0);
            Assert.IsTrue(sim.Lost);
            Assert.IsFalse(sim.Move(1), "nothing moves after the end");
        }
    }
}
