using System;
using System.Collections.Generic;
using System.Linq;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Animation durations that time game logic: native objects act when their scenes finish, so the simulation
    /// needs the Windows timelines' lengths. Without them, as in tests, those scenes never finish on their own.</summary>
    /// <param name="Character">Om Nom scene durations by scene name, from animation 85.</param>
    /// <param name="Fruit">Fruit scene durations by native fruit type, from animation 30 (types 0, 1 and 2 play scenes 0, 2 and 1).</param>
    /// <param name="Fireball">Fireball scene durations by native scene index, from animation 25.</param>
    /// <param name="Booka">Booka scene durations by native scene index, from animation 13.</param>
    public sealed record SceneTimings(IReadOnlyDictionary<string, double>? Character = null, IReadOnlyList<double>? Fruit = null,
        IReadOnlyList<double>? Fireball = null, IReadOnlyList<double>? Booka = null)
    {
        /// <summary>Windows animation resource of Om Nom.</summary>
        public const int CharacterAnimationId = 85;

        /// <summary>Windows animation resource of the fruits: scenes 0, 1 and 2 are the apple, grape and strawberry.</summary>
        public const int FruitAnimationId = 30;

        /// <summary>Windows animation resource of the fireball: IDLE, IDLE_2, FADE_WATER and FADE_FIRE.</summary>
        public const int FireballAnimationId = 25;

        /// <summary>Windows animation resource of the Booka: IDLE, KNOT_IDLE, ACTION and KNOT_ACTION.</summary>
        public const int BookaAnimationId = 13;

        /// <summary>Loads every timing the simulation uses.</summary>
        /// <param name="cache">Local game cache.</param>
        /// <returns>All timings.</returns>
        public static SceneTimings LoadAll(string cache)
        {
            return new(LoadCharacter(cache), LoadFruit(cache), LoadFireball(cache), LoadScenes(cache, BookaAnimationId));
        }

        /// <summary>Loads Om Nom's timings and only the object timings a level needs in a variant.</summary>
        /// <param name="cache">Local game cache.</param>
        /// <param name="level">Level to play.</param>
        /// <param name="variant">Game mode to play it in.</param>
        /// <returns>The needed timings.</returns>
        public static SceneTimings Load(string cache, LevelDefinition level, LevelVariant variant)
        {
            ArgumentNullException.ThrowIfNull(level);
            return new(LoadCharacter(cache), variant == LevelVariant.FruitChallenge ? LoadFruit(cache) : null,
                level.Objects.Any(o => o.Kind == "fireball") ? LoadFireball(cache) : null,
                level.Objects.Any(o => o.Kind == "booka") ? LoadScenes(cache, BookaAnimationId) : null);
        }

        /// <summary>Loads Om Nom's scene durations by name.</summary>
        /// <param name="cache">Local game cache.</param>
        /// <returns>Durations in seconds.</returns>
        public static IReadOnlyDictionary<string, double> LoadCharacter(string cache)
        {
            return AnimationPose.SceneDurations(AnimationReader.LoadWindows(cache, CharacterAnimationId));
        }

        private static double[] LoadFruit(string cache)
        {
            AnimationElement root = AnimationReader.LoadWindows(cache, FruitAnimationId);
            return [AnimationPose.Duration(root, 0), AnimationPose.Duration(root, 2), AnimationPose.Duration(root, 1)];
        }

        private static double[] LoadFireball(string cache)
        {
            return LoadScenes(cache, FireballAnimationId);
        }

        private static double[] LoadScenes(string cache, int animation)
        {
            AnimationElement root = AnimationReader.LoadWindows(cache, animation);
            return [.. Enumerable.Range(0, 4).Select(scene => AnimationPose.Duration(root, scene))];
        }

        /// <summary>Counts the fixed updates a timeline started before a step's updates takes to finish: it first advances in
        /// that step and finishes on the first update at or beyond its duration.</summary>
        /// <param name="duration">Timeline length in seconds.</param>
        /// <returns>Updates, at least one.</returns>
        internal static int Updates(double duration)
        {
            int updates = 1;
            while (updates * (double)PhysicsWorld.TimeStep < duration)
            {
                updates++;
            }
            return updates;
        }
    }
}
