using System.Collections.Generic;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Water sound cues follow native callbacks without introducing world bodies.</summary>
    public sealed class WaterAudioTests
    {
        /// <summary>Simulation exposes an event boundary for the interactive audio host.</summary>
        [Fact]
        public void ExposesSoundCuesWithoutRequiringAnAudioDevice()
        {
            Assert.NotNull(typeof(CandySimulation).GetEvent("SoundPlayed"));
        }

        /// <summary>Splash and audio share the same first-frame, speed and cooldown decision.</summary>
        [Fact]
        public void EntryEmitsOneFullVolumeSplashAndTouchesAreSilent()
        {
            CandySimulation scene = WaterTests.Create("<candy x='100' y='20'/><water y='100'/>");
            List<SimulationSound> sounds = [];
            scene.SoundPlayed += sounds.Add;
            scene.Step();
            Assert.False(scene.Tap(new(100, 500)));
            Assert.Empty(sounds);
            PhysicsBody body = scene.Candies[0];
            scene.World.SetTransform(body, new(1, 3), 0);
            body.SetLinearVelocity(new(0, 2));
            scene.Step();
            SimulationSound sound = Assert.Single(sounds);
            Assert.Contains(sound.Sound, new[] { SimulationSoundId.WaterSplash1, SimulationSoundId.WaterSplash2 });
            Assert.Equal(scene.Tick, sound.Tick);
            Assert.Equal(1, sound.Volume);
            scene.Step();
            _ = Assert.Single(sounds);
            Assert.Equal(10, scene.WaterEffects!.Drops.Count);
        }

        /// <summary>Water entry extinguishes once, including initial immersion, without a splash cue.</summary>
        [Fact]
        public void FireballPlaysDrowningOnItsFirstUnderwaterCallback()
        {
            CandySimulation scene = WaterTests.Create("<candy x='20' y='20'/><fireball x='200' y='250'/><water y='100'/>");
            List<SimulationSound> sounds = [];
            scene.SoundPlayed += sounds.Add;
            scene.Step();
            scene.Step();
            Assert.Equal(SimulationSoundId.FireDrown, Assert.Single(sounds).Sound);
            Assert.Empty(scene.WaterEffects!.Drops);
        }

        /// <summary>Listening to sounds preserves all native physics trace components.</summary>
        [Fact]
        public void AudioRandomnessDoesNotChangePhysicsOrVisualRandomness()
        {
            CandySimulation a = WaterTests.Create("<candy x='100' y='20'/><water y='100'/>");
            CandySimulation b = WaterTests.Create("<candy x='100' y='20'/><water y='100'/>");
            a.SoundPlayed += _ => { };
            for (int i = 0; i < 300; i++)
            {
                a.Step();
                b.Step();
                Assert.Equal(b.Candies[0].Position, a.Candies[0].Position);
                Assert.Equal(b.Candies[0].Velocity, a.Candies[0].Velocity);
                Assert.Equal(b.WaterEffects!.Drops, a.WaterEffects!.Drops);
                Assert.Equal(b.WaterEffects.Lights, a.WaterEffects.Lights);
            }
        }
    }
}
