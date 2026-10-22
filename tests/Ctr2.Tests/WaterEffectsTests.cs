using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Windows water decoration contracts.</summary>
    public sealed class WaterEffectsTests
    {
        /// <summary>Visual state has no body or journal slot.</summary>
        [Fact]
        public void WaterOwnsVisualEffectsOutsideThePhysicsWorld()
        {
            CandySimulation scene = WaterTests.Create("<candy x='100' y='100'/><water y='100'/>");
            Assert.NotNull(scene.WaterEffects);
            CandySimulation dry = WaterTests.Create("<candy x='100' y='100'/>");
            Assert.Null(dry.WaterEffects);
            Assert.Equal(dry.World.Bodies.Count, scene.World.Bodies.Count);
        }

        /// <summary>Touches below water create short systems and never consume scene input.</summary>
        [Fact]
        public void TouchBubblesEmitBeforeIntegrationAndExpire()
        {
            CandySimulation scene = WaterTests.Create("<candy x='100' y='20'/><water y='100'/>");
            WaterEffects effects = scene.WaterEffects!;
            Assert.False(scene.Tap(new(300, 200)));
            Assert.Empty(effects.Bubbles);
            Assert.False(scene.Tap(new(300, 500)));
            WaterParticle initial = Assert.Single(effects.Bubbles);
            Assert.InRange(initial.Position.Y, 470, 530);
            Assert.InRange(initial.Scale.X, .5f, 1);
            Assert.Equal(initial.Scale.X, initial.Scale.Y);
            for (int i = 0; i < 5; i++)
            {
                effects.Step();
            }

            _ = Assert.Single(effects.Bubbles);
            effects.Step();
            effects.Step();
            Assert.Equal(2, effects.Bubbles.Count);
            Assert.All(effects.Bubbles, p => Assert.InRange(p.Quad, 0, 2));
            for (int i = 0; i < 160; i++)
            {
                effects.Step();
            }

            Assert.Empty(effects.Bubbles);
            Assert.InRange(initial.Position.Y, 470, 530); // Prior snapshots do not mutate.
        }

        /// <summary>Ambient emission begins in the Windows four-to-seven-second window and uses long life.</summary>
        [Fact]
        public void AmbientBubblesUseWindowsLongDuration()
        {
            WaterEffects effects = new(new(640, 960), 200);
            int tick = 0;
            while (effects.Bubbles.Count == 0 && tick < 421)
            {
                effects.Step();
                tick++;
            }
            Assert.InRange(tick, 240, 420);
            for (int i = 0; i < 120; i++)
            {
                effects.Step();
            }

            Assert.Equal(3, effects.Bubbles.Count); // Short 1.2s systems have already stopped.
        }

        /// <summary>Immersed fast bodies splash only after the initial frame; sprite callbacks lag their color by one update.</summary>
        [Fact]
        public void SplashSkipsInitialFrameAndUsesPreviousCallbackColor()
        {
            CandySimulation scene = WaterTests.Create("<candy x='100' y='200'/><water y='100'/>");
            PhysicsBody body = scene.Candies[0];
            body.SetLinearVelocity(new(0, 2));
            WaterEffects first = new(new(640, 960), 200);
            first.Observe(body, true);
            first.EndStep();
            Assert.Empty(first.Drops);
            WaterEffects effects = new(new(640, 960), 200);
            effects.EndStep();
            effects.Observe(body, true);
            effects.EndStep();
            Assert.Equal(10, effects.Drops.Count);
            Assert.All(effects.Drops, p => Assert.Equal(0, p.Opacity));
            effects.Step();
            Assert.Contains(effects.Drops, p => p.Opacity == 1);
            Assert.All(effects.Drops, p => Assert.InRange(p.Quad, 9, 11));
            for (int i = 0; i < 80; i++)
            {
                effects.Step();
            }

            Assert.Empty(effects.Drops);
        }

        /// <summary>Both the strict speed threshold and recent-immersion suppression precede another burst.</summary>
        [Fact]
        public void SplashRequiresSpeedAndASeparateEntry()
        {
            CandySimulation scene = WaterTests.Create("<candy x='100' y='200'/><water y='100'/>");
            WaterEffects effects = scene.WaterEffects!;
            PhysicsBody body = scene.Candies[0];
            effects.EndStep();
            body.SetLinearVelocity(new(0, 1.7f));
            effects.Observe(body, true);
            effects.EndStep();
            Assert.Empty(effects.Drops);
            effects.Observe(body, false);
            for (int i = 0; i < 4; i++)
            {
                effects.Step();
            }

            body.SetLinearVelocity(new(0, 2));
            effects.Observe(body, true);
            effects.EndStep();
            Assert.Empty(effects.Drops); // DeltaCollision also remembers the slow entry.
            effects.Observe(body, false);
            for (int i = 0; i < 4; i++)
            {
                effects.Step();
            }

            scene.World.SetTransform(body, body.Origin + new Vector2(.1f, 0), 0);
            effects.Observe(body, true);
            effects.EndStep();
            Assert.Equal(10, effects.Drops.Count);
        }

        /// <summary>Visual random sampling and effects do not write body state.</summary>
        [Fact]
        public void EffectsAreRepeatableWithoutChangingPhysics()
        {
            CandySimulation a = WaterTests.Create("<candy x='100' y='250'/><water y='100'/>");
            CandySimulation b = WaterTests.Create("<candy x='100' y='250'/><water y='100'/>");
            for (int i = 0; i < 600; i++)
            {
                if (i % 60 == 0)
                {
                    _ = a.Tap(new(200, 500));
                }

                a.Step();
                b.Step();
                Assert.Equal(b.Candies[0].Position, a.Candies[0].Position);
                Assert.Equal(b.Candies[0].Velocity, a.Candies[0].Velocity);
            }
            WaterEffects c = new(new(640, 960), 200), d = new(new(640, 960), 200);
            for (int i = 0; i < 900; i++)
            {
                c.Step();
                d.Step();
            }
            Assert.Equal(c.Lights.ToArray(), d.Lights.ToArray());
            Assert.Equal(c.Bubbles.ToArray(), d.Bubbles.ToArray());
            Assert.All(c.Lights, l => { Assert.InRange(l.Quad, 3, 4); Assert.InRange(l.Opacity, 0, .4f); });
        }

        /// <summary>The extra surface fade reaches zero five logical units beneath the water origin.</summary>
        [Fact]
        public void BubblesFadeAtTheSurfaceWithoutCompoundingTheirColor()
        {
            WaterEffects effects = new(new(640, 960), 200);
            effects.Touch(new(320, 225));
            bool sawFade = false;
            for (int i = 0; i < 80; i++)
            {
                effects.Step();
                foreach (WaterParticle bubble in effects.Bubbles)
                {
                    if (bubble.Position.Y <= 205)
                    {
                        Assert.Equal(0, bubble.Opacity);
                        sawFade = true;
                    }
                    if (bubble.Position.Y > 217)
                    {
                        Assert.True(bubble.Opacity > 0);
                    }
                }
            }
            Assert.True(sawFade);
        }

        /// <summary>The per-frame fireball callback clears velocity before the splash speed test.</summary>
        [Fact]
        public void ExtinguishedFireballDoesNotSplash()
        {
            CandySimulation scene = WaterTests.Create("<candy x='20' y='20'/><fireball x='200' y='20'/><water y='100'/>");
            scene.Step();
            PhysicsBody body = scene.Fireballs[0].Body;
            scene.World.SetTransform(body, new(2, 3), 0);
            body.SetLinearVelocity(new(0, 3));
            scene.Step();
            Assert.Equal(Vector2.Zero, body.Velocity);
            Assert.Empty(scene.WaterEffects!.Drops);
        }
    }
}
