using System;
using System.Collections.Generic;
using System.Linq;

using Ctr2.Desktop;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Invented PCM verifies mixing and scene lifecycle without opening a device.</summary>
    public sealed class WaterAudioPlaybackTests
    {
        /// <summary>Loops wrap, mono duplicates, overlapping voices add, and finished voices retire.</summary>
        [Fact]
        public void MixesLoopingMonoAndOverlappingStereo()
        {
            WaterAudioMixer mixer = new();
            mixer.Play(new([.1f, .2f], 1, 48000), true, 1);
            mixer.Play(new([.3f, .4f, .5f, .6f], 2, 48000), false, 1);
            float[] pcm = new float[8];
            mixer.Render(pcm);
            Assert.Equal(new float[] { .4f, .5f, .7f, .8f, .1f, .1f, .2f, .2f }, pcm);
            mixer.Clear();
            mixer.Render(pcm);
            Assert.All(pcm, sample => Assert.Equal(0, sample));
        }

        /// <summary>Rate conversion interpolates in source sample frames and duplicates mono.</summary>
        [Fact]
        public void ResamplesAtTheDecodedRateAndClipsTheMixedOutput()
        {
            WaterAudioMixer mixer = new();
            mixer.Play(new([0, 1, 0], 1, 24000), false, 1);
            float[] pcm = new float[8];
            mixer.Render(pcm);
            Assert.Equal(new float[] { 0, 0, .5f, .5f, 1, 1, .5f, .5f }, pcm);
            mixer.Clear();
            mixer.Play(new([.8f], 1, 48000), true, 1);
            mixer.Play(new([.8f], 1, 48000), true, 1);
            mixer.Render(pcm);
            Assert.All(pcm, sample => Assert.Equal(1, sample));
        }

        /// <summary>Restart drops old voices and subscriptions; pause retains phase and buffered audio.</summary>
        [Fact]
        public void SceneBindingHandlesPauseRestartAndDisposal()
        {
            Output output = new();
            List<SimulationSoundId> loaded = [];
            using WaterAudioPlayback audio = new(output, id =>
            {
                loaded.Add(id);
                return new([.1f], 1, 48000);
            });
            CandySimulation scene = WaterTests.Create("<candy x='100' y='20'/><water y='100'/>");
            audio.Update(scene, false);
            Assert.Equal(new[] { SimulationSoundId.WaterAmbience }, loaded);
            Assert.True(output.QueuedFrames >= 2048);
            int frames = output.QueuedFrames;
            audio.Update(scene, true);
            Assert.True(output.Paused);
            Assert.Equal(frames, output.QueuedFrames);
            audio.Update(scene, false);
            Assert.False(output.Paused);
            _ = Assert.Single(loaded);
            CandySimulation dry = WaterTests.Create("<candy x='100' y='20'/>");
            audio.Update(dry, false);
            Assert.Equal(0, output.QueuedFrames);
            scene.Step();
            scene.World.SetTransform(scene.Candies[0], new(1, 3), 0);
            scene.Candies[0].SetLinearVelocity(new(0, 2));
            scene.Step();
            _ = Assert.Single(loaded); // Old scene was detached.
            audio.Update(WaterTests.Create("<candy x='100' y='20'/><water y='100'/>"), false);
            Assert.Equal(1, loaded.Count(id => id == SimulationSoundId.WaterAmbience));
            Assert.Equal(3, output.ClearCount);
            audio.Dispose();
            Assert.True(output.Disposed);
        }

        private sealed class Output : IWaterAudioOutput
        {
            public int QueuedFrames
            {
                get; private set;
            }
            public bool Paused
            {
                get; set;
            }
            internal bool Disposed;
            internal int ClearCount;
            public void Queue(ReadOnlySpan<float> samples)
            {
                QueuedFrames += samples.Length / 2;
            }

            public void Clear()
            {
                QueuedFrames = 0;
                ClearCount++;
            }
            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
