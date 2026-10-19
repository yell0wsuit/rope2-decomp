using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks explicit first-entry arrival selection and deterministic nonphysical leaf presentation.</summary>
    public sealed class FirstLevelArrivalTests
    {
        internal static GameSession Create(bool arrival, int y = -97)
        {
            LevelDefinition level = LevelReader.Read($"<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='280' y='0'/><omnom x='100' y='{y}' gravityScale='1'/><tutorial_swipe x='150' y='70' delay='3' angle='1'/><polygon_materialized x='0' y='250' material='wood' polygon='320,0,320,30,0,30,' smoothFactor='1'/></layer></map>");
            LevelDefinition design = LevelReader.Read(CandySimulationTests.Design.Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"")
                .Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "<polygon id='wood' body_type='static' friction='0.4' restitution='0' isSensor='false'/></layer></map>"));
            return new(level, design, firstLevelArrival: arrival);
        }

        /// <summary>Only explicitly selected first-entry arrival permits its authored off-screen character.</summary>
        [Fact]
        public void ExplicitArrivalFallsFromAuthoredNegativeSpawn()
        {
            _ = Assert.Throws<System.IO.InvalidDataException>(() => Create(false));
            GameSession session = Create(true);
            Assert.Equal(new Vector2(1, -.97f), session.Simulation.Characters[0].Origin);
            Assert.Equal(30, session.Simulation.Gameplay!.ArrivalLeaves!.Particles.Count);
            session.Step();
            Assert.Equal(LevelResult.Running, session.Simulation.Gameplay.Result);
            bool fell = false;
            for (int i = 0; i < 180; i++)
            {
                session.Step();
                fell |= session.Simulation.Gameplay.CharacterState == CharacterState.Falling;
            }
            Assert.True(fell);
            Assert.Equal(CharacterState.Idle, session.Simulation.Gameplay.CharacterState);
            Assert.Equal(LevelResult.Running, session.Simulation.Gameplay.Result);
        }

        /// <summary>Arrival leaves expire after a bounded lifetime and retain immutable initial snapshots.</summary>
        [Fact]
        public void BurstUsesBoundedSourceRangesAndExpires()
        {
            ForestLeafBurst burst = new(new Vector2(50, 100));
            ForestLeaf[] initial = [.. burst.Particles];
            Assert.Equal(30, initial.Length);
            foreach (ForestLeaf particle in initial)
            {
                Assert.InRange(particle.Position.X, 49, 51);
                Assert.InRange(particle.Position.Y, 99, 101);
                Assert.InRange(particle.Scale.X, .9f, 1.5f);
                Assert.InRange(particle.Scale.Y, .9f, 1.5f);
                Assert.InRange(particle.Variant, 0, 2);
                Assert.Equal(1, particle.Opacity);
            }
            burst.Step();
            Assert.NotEqual(initial[0].Position, burst.Particles[0].Position);
            Assert.Equal(1, initial[0].Opacity);
            for (int i = 0; i < 90; i++)
            {
                burst.Step();
            }
            Assert.Empty(burst.Particles);
        }

        /// <summary>Leaves cannot change physical motion, and restarting reconstructs the same burst.</summary>
        [Fact]
        public void PresentationIsDeterministicAndDoesNotChangePhysics()
        {
            GameSession arrival = Create(true, 0), ordinary = Create(false, 0);
            ForestLeaf[] initial = [.. arrival.Simulation.Gameplay!.ArrivalLeaves!.Particles];
            for (int i = 0; i < 60; i++)
            {
                arrival.Step();
                ordinary.Step();
                Assert.Equal(ordinary.Simulation.Characters[0].Position, arrival.Simulation.Characters[0].Position);
                Assert.Equal(ordinary.Simulation.Candies[0].Position, arrival.Simulation.Candies[0].Position);
            }
            arrival.Restart();
            Assert.Equal(initial, arrival.Simulation.Gameplay!.ArrivalLeaves!.Particles.ToArray());
            GameSession batched = Create(true, 0);
            batched.AdvanceTo(60 * (double)PhysicsWorld.TimeStep);
            for (int i = 0; i < 60; i++)
            {
                arrival.Step();
            }
            Assert.Equal(arrival.Simulation.Gameplay.ArrivalLeaves.Particles.ToArray(), batched.Simulation.Gameplay!.ArrivalLeaves!.Particles.ToArray());
            Assert.Null(ordinary.Simulation.Gameplay!.ArrivalLeaves);
        }

        /// <summary>Swipe hints use the authored delay and fade only after an active touch.</summary>
        [Fact]
        public void SwipeHintDelayAndDismissalMatchSource()
        {
            SwipeTutorial early = new(new(100, 200), 1, .1f);
            early.TouchDown();
            for (int i = 0; i < 60; i++)
            {
                early.Step();
            }
            Assert.Equal(0, early.Opacity);
            SwipeTutorial active = new(new(100, 200), 1, .1f);
            for (int i = 0; i < 5; i++)
            {
                active.Step();
            }
            Assert.Equal(0, active.Opacity);
            active.Step();
            Assert.Equal(1, active.Opacity);
            active.TouchDown();
            for (int i = 0; i < 12; i++)
            {
                active.Step();
            }
            Assert.InRange(active.Opacity, .49f, .51f);
            for (int i = 0; i < 12; i++)
            {
                active.Step();
            }
            Assert.Equal(0, active.Opacity);
        }
    }
}
