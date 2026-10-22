using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Ctr2.Engine.Physics;

using NVorbis;

using SDL3;

namespace Ctr2.Desktop
{
    internal sealed record WaterAudioClip(float[] Samples, int Channels, int Rate)
    {
        internal static WaterAudioClip Load(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("Water sound exceeds decoding bounds.");
            }
            using VorbisReader reader = new(path);
            if (reader.Channels is < 1 or > 2 || reader.SampleRate is < 8000 or > 192000 ||
                reader.TotalSamples <= 0 || reader.TotalSamples > (long)reader.SampleRate * 120)
            {
                throw new InvalidDataException("Water sound format exceeds decoding bounds.");
            }
            float[] samples = new float[checked((int)reader.TotalSamples * reader.Channels)];
            int count = 0;
            while (count < samples.Length)
            {
                int read = reader.ReadSamples(samples, count, samples.Length - count);
                if (read == 0)
                {
                    throw new InvalidDataException("Truncated water sound.");
                }
                count += read;
            }
            return new(samples, reader.Channels, reader.SampleRate);
        }
    }

    internal sealed class WaterAudioMixer
    {
        private readonly List<Voice> voices = [];
        internal void Play(WaterAudioClip clip, bool loop, float volume)
        {
            voices.Add(new(clip, loop, volume));
        }
        internal void Clear()
        {
            voices.Clear();
        }
        internal bool Active => voices.Count > 0;
        internal void Render(Span<float> destination)
        {
            destination.Clear();
            foreach (Voice voice in voices)
            {
                int frames = voice.Clip.Samples.Length / voice.Clip.Channels;
                for (int frame = 0; frame < destination.Length / 2; frame++)
                {
                    if (voice.Position >= frames)
                    {
                        if (!voice.Loop)
                        {
                            break;
                        }
                        voice.Position %= frames;
                    }
                    int first = (int)voice.Position;
                    int next = first + 1 < frames ? first + 1 : voice.Loop ? 0 : first;
                    float fraction = (float)(voice.Position - first);
                    for (int channel = 0; channel < 2; channel++)
                    {
                        int c = voice.Clip.Channels == 1 ? 0 : channel;
                        float a = voice.Clip.Samples[(first * voice.Clip.Channels) + c];
                        float b = voice.Clip.Samples[(next * voice.Clip.Channels) + c];
                        destination[(frame * 2) + channel] += (a + ((b - a) * fraction)) * voice.Volume;
                    }
                    voice.Position += voice.Clip.Rate / 48000.0;
                }
            }
            _ = voices.RemoveAll(v => !v.Loop && v.Position >= v.Clip.Samples.Length / v.Clip.Channels);
            for (int i = 0; i < destination.Length; i++)
            {
                destination[i] = Math.Clamp(destination[i], -1, 1);
            }
        }
        private sealed class Voice(WaterAudioClip clip, bool loop, float volume)
        {
            internal readonly WaterAudioClip Clip = clip;
            internal readonly bool Loop = loop;
            internal readonly float Volume = volume;
            internal double Position;
        }
    }

    internal interface IWaterAudioOutput : IDisposable
    {
        int QueuedFrames
        {
            get;
        }
        bool Paused
        {
            get; set;
        }
        void Queue(ReadOnlySpan<float> samples);
        void Clear();
    }

    internal sealed class WaterAudioPlayback(IWaterAudioOutput output, Func<SimulationSoundId, WaterAudioClip> load) : IDisposable
    {
        private readonly WaterAudioMixer mixer = new();
        private readonly Dictionary<SimulationSoundId, WaterAudioClip> clips = [];
        private readonly float[] block = new float[1024];
        private CandySimulation? attached;
        private bool disposed;

        internal static WaterAudioPlayback Create(string cache)
        {
            Dictionary<SimulationSoundId, WaterAudioClip> decoded = [];
            foreach ((SimulationSoundId id, string name) in new[] { (SimulationSoundId.WaterAmbience, "water_splash_idle.ogg"),
                (SimulationSoundId.WaterSplash1, "water_splash01.ogg"), (SimulationSoundId.WaterSplash2, "water_splash02.ogg"),
                (SimulationSoundId.FireDrown, "fire_drown01.ogg") })
            {
                decoded.Add(id, WaterAudioClip.Load(Path.Combine(cache, "assets", name)));
            }
            return new(new SdlWaterAudioOutput(), id => decoded[id]);
        }

        private WaterAudioClip Clip(SimulationSoundId id)
        {
            if (!clips.TryGetValue(id, out WaterAudioClip? clip))
            {
                clip = load(id);
                clips.Add(id, clip);
            }
            return clip;
        }
        private void OnSound(SimulationSound sound)
        {
            mixer.Play(Clip(sound.Sound), false, sound.Volume);
        }
        internal void Update(CandySimulation scene, bool paused)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (attached != scene)
            {
                attached?.SoundPlayed -= OnSound;
                attached = scene;
                output.Clear();
                mixer.Clear();
                scene.SoundPlayed += OnSound;
                if (scene.WaterEffects != null)
                {
                    mixer.Play(Clip(SimulationSoundId.WaterAmbience), true, 1);
                }
            }
            output.Paused = paused;
            if (!paused)
            {
                while (mixer.Active && output.QueuedFrames < 2048)
                {
                    mixer.Render(block);
                    output.Queue(block);
                }
            }
        }
        public void Dispose()
        {
            if (!disposed)
            {
                attached?.SoundPlayed -= OnSound;
                output.Dispose();
                disposed = true;
            }
        }
    }

    internal sealed class SdlWaterAudioOutput : IWaterAudioOutput
    {
        private nint stream;

        internal SdlWaterAudioOutput()
        {
            Check(SDL.InitSubSystem(SDL.InitFlags.Audio));
            SDL.AudioSpec spec = new()
            {
                Channels = 2,
                Freq = 48000,
                Format = SDL.AudioFormat.AudioF32LE
            };
            stream = SDL.OpenAudioDeviceStream(SDL.AudioDeviceDefaultPlayback, in spec, null, 0);
            if (stream == 0)
            {
                string error = SDL.GetError();
                SDL.QuitSubSystem(SDL.InitFlags.Audio);
                throw new InvalidOperationException(error);
            }
        }
        public int QueuedFrames
        {
            get
            {
                int queued = SDL.GetAudioStreamQueued(stream);
                return queued < 0 ? throw new InvalidOperationException(SDL.GetError()) : queued / 8;
            }
        }
        public bool Paused
        {
            get;
            set
            {
                if (field != value)
                {
                    Check(value ? SDL.PauseAudioStreamDevice(stream) : SDL.ResumeAudioStreamDevice(stream));
                    field = value;
                }
            }
        } = true;
        public void Queue(ReadOnlySpan<float> samples)
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(samples);
            Check(SDL.PutAudioStreamData(stream, bytes, bytes.Length));
        }
        public void Clear()
        {
            Check(SDL.ClearAudioStream(stream));
        }
        public void Dispose()
        {
            if (stream != 0)
            {
                SDL.DestroyAudioStream(stream);
                stream = 0;
                SDL.QuitSubSystem(SDL.InitFlags.Audio);
            }
        }
        private static void Check(bool result)
        {
            if (!result)
            {
                throw new InvalidOperationException(SDL.GetError());
            }
        }
    }
}
