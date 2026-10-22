using System;

namespace Ctr2.Engine.Physics
{
    /// <summary>Windows resource IDs used by water callbacks.</summary>
    public enum SimulationSoundId
    {
        /// <summary>Fireball's first underwater callback.</summary>
        FireDrown = 255,
        /// <summary>Water scene ambience, looping until the scene ends.</summary>
        WaterAmbience = 323,
        /// <summary>First fast-entry variant.</summary>
        WaterSplash1 = 324,
        /// <summary>Second fast-entry variant.</summary>
        WaterSplash2 = 325
    }

    /// <summary>A sound request on the fixed simulation clock, without an audio device dependency.</summary>
    public sealed record SimulationSound(int Tick, SimulationSoundId Sound, float Volume);

    public sealed partial class CandySimulation
    {
        private uint soundRandom = 0x41554449;
        /// <summary>Raised synchronously by a source sound trigger; an interactive host owns playback.</summary>
        public event Action<SimulationSound>? SoundPlayed;

        private void PlaySound(SimulationSoundId sound)
        {
            SoundPlayed?.Invoke(new(Tick, sound, 1));
        }

        private void PlayWaterSplash()
        {
            soundRandom ^= soundRandom << 13;
            soundRandom ^= soundRandom >> 17;
            soundRandom ^= soundRandom << 5;
            PlaySound((soundRandom & 1) == 0 ? SimulationSoundId.WaterSplash1 : SimulationSoundId.WaterSplash2);
        }
    }
}
