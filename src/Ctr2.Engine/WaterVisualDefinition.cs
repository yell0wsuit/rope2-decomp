using System;
using System.Collections.Generic;

using System.Linq;

using Ctr2.Assets;

namespace Ctr2.Engine
{
    /// <summary>Windows water artwork parameters, separate from body physics and authored placement metadata.</summary>
    /// <param name="Level">Initial surface in logical units.</param>
    /// <param name="Speed">Horizontal wave speed in logical units per second.</param>
    /// <param name="MinimumScale">Wave minimum vertical scale.</param>
    /// <param name="MaximumScale">Wave maximum vertical scale.</param>
    /// <param name="ScaleTime">Duration of each scale leg.</param>
    public sealed record WaterVisualDefinition(float Level, float Speed, float MinimumScale, float MaximumScale, float ScaleTime)
    {
        /// <summary>Reads the last water placement and the Windows master graphics design, or null for a dry map.</summary>
        /// <param name="level">Authored level.</param>
        /// <param name="design">Matching master design.</param>
        /// <returns>Parsed visual parameters, without creating or modifying simulation bodies.</returns>
        public static WaterVisualDefinition? Load(LevelDefinition level, LevelDefinition design)
        {
            ArgumentNullException.ThrowIfNull(level);
            ArgumentNullException.ThrowIfNull(design);
            LevelElement? water = level.Objects.LastOrDefault(o => o.Kind == "water");
            if (water == null)
            {
                return null;
            }
            LevelElement? fields = design.Objects.FirstOrDefault(o => o.Attributes.GetValueOrDefault("id") == "water_design");
            float Value(string key)
            {
                return NativeText.FloatValue(fields?.Attributes.GetValueOrDefault(key));
            }
            return new(NativeText.FloatValue(water.Attributes.GetValueOrDefault("y")) * 2, Value("gfx_wave_speed"),
                Value("gfx_wave_scale_y_min"), Value("gfx_wave_scale_y_max"), Value("gfx_wave_scale_time"));
        }
    }
}
