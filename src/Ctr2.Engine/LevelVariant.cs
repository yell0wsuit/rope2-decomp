using System;

namespace Ctr2.Engine
{
    /// <summary>The game mode a level is played in, which native <c>GameScene::loadMap</c> reads from
    /// <c>GameState::getChallengeType</c> while building the scene.</summary>
    public enum LevelVariant
    {
        /// <summary>The ordinary level with its stars and tutorial fingers.</summary>
        Main,
        /// <summary>The medal challenge: the same scene without tutorial fingers.</summary>
        Challenge,
        /// <summary>The fruit challenge: the stars are removed, a fruit grid is added and tutorial fingers are left out.</summary>
        FruitChallenge
    }

    /// <summary>Converts between variants and their journal file suffixes.</summary>
    public static class LevelVariants
    {
        /// <summary>Parses <c>main</c>, <c>challenge</c> or <c>fruit_challenge</c>.</summary>
        /// <param name="name">Variant name.</param>
        /// <returns>The variant.</returns>
        /// <exception cref="ArgumentException">The name is not a variant.</exception>
        public static LevelVariant Parse(string name)
        {
            return name switch
            {
                "main" => LevelVariant.Main,
                "challenge" => LevelVariant.Challenge,
                "fruit_challenge" => LevelVariant.FruitChallenge,
                _ => throw new ArgumentException("Variant must be main, challenge or fruit_challenge.", nameof(name))
            };
        }

        /// <summary>Gets the variant's name as hint journal file names spell it.</summary>
        /// <param name="variant">Variant.</param>
        /// <returns><c>main</c>, <c>challenge</c> or <c>fruit_challenge</c>.</returns>
        public static string Name(this LevelVariant variant)
        {
            return variant switch
            {
                LevelVariant.Challenge => "challenge",
                LevelVariant.FruitChallenge => "fruit_challenge",
                LevelVariant.Main => "main",
                _ => throw new ArgumentOutOfRangeException(nameof(variant))
            };
        }
    }
}
