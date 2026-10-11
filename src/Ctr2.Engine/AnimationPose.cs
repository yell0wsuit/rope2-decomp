using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Framework;

namespace Ctr2.Engine
{
    /// <summary>A sampled image part in authored draw order.</summary>
    /// <param name="Source">Original image resource ID.</param>
    /// <param name="Quad">Selected quad index.</param>
    /// <param name="Transform">Transform from trimmed image coordinates to root logical coordinates.</param>
    /// <param name="Color">RGBA tint components.</param>
    public sealed record AnimationSprite(int Source, int Quad, Matrix4x4 Transform, Vector4 Color);

    /// <summary>Evaluates an authored scene deterministically without gameplay callbacks or a graphics API.</summary>
    public static class AnimationPose
    {
        /// <summary>Samples one named-by-ID scene, holding each track's terminal pose.</summary>
        /// <param name="root">Decoded animation tree.</param>
        /// <param name="scene">Original scene ID.</param>
        /// <param name="seconds">Nonnegative finite time since scene start.</param>
        /// <param name="quadSize">Returns trimmed logical dimensions for an image resource and quad.</param>
        /// <returns>Visible image parts in authored order.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Time is negative or nonfinite.</exception>
        /// <exception cref="ArgumentException">The root has no requested scene.</exception>
        public static IReadOnlyList<AnimationSprite> Sample(AnimationElement root, int scene, double seconds,
            Func<int, int, Vector2> quadSize)
        {
            if (!double.IsFinite(seconds) || seconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            if (!root.Timelines.Any(t => t.Id == scene && !t.IsEmpty))
            {
                throw new ArgumentException("Animation has no requested scene.", nameof(scene));
            }
            List<AnimationSprite> sprites = [];
            Visit(root, scene, seconds, quadSize, Vector2.Zero, Vector2.Zero, Matrix4x4.Identity, Vector4.One, sprites, 0);
            return sprites;
        }

        /// <summary>Returns the longest track duration in a scene across the whole tree.</summary>
        /// <param name="root">Animation tree.</param>
        /// <param name="scene">Original scene ID.</param>
        /// <returns>Scene duration in seconds, or zero for a static scene.</returns>
        public static double Duration(AnimationElement root, int scene)
        {
            double duration = root.Timelines.Where(t => t.Id == scene && !t.IsEmpty)
                .SelectMany(t => t.Tracks).GroupBy(t => t.Type).Select(t => t.SelectMany(track => track.Keys).Sum(k => (double)k.Time)).DefaultIfEmpty().Max();
            foreach (AnimationElement child in root.Children)
            {
                duration = Math.Max(duration, Duration(child, scene));
            }
            return duration;
        }

        /// <summary>Samples and transforms one element, preserving absolute pre-transform child positions.</summary>
        /// <param name="element">Element to evaluate.</param>
        /// <param name="scene">Scene ID.</param>
        /// <param name="seconds">Elapsed scene time.</param>
        /// <param name="quadSize">Trimmed size resolver.</param>
        /// <param name="parentPosition">Parent's untransformed top-left point.</param>
        /// <param name="parentSize">Parent's logical size.</param>
        /// <param name="parentTransform">Inherited drawing matrix.</param>
        /// <param name="parentColor">Inherited RGBA tint.</param>
        /// <param name="sprites">Ordered output parts.</param>
        /// <param name="depth">Current element depth.</param>
        private static void Visit(AnimationElement element, int scene, double seconds, Func<int, int, Vector2> quadSize,
            Vector2 parentPosition, Vector2 parentSize, Matrix4x4 parentTransform, Vector4 parentColor, List<AnimationSprite> sprites, int depth)
        {
            if (depth >= 64)
            {
                throw new ArgumentException("Animation nesting exceeds 64 elements.");
            }
            AnimationTimeline? timeline = element.Timelines.FirstOrDefault(t => t.Id == scene);
            if (timeline?.IsEmpty == true)
            {
                return;
            }
            Vector2 position = Vector2.Zero, scale = Vector2.One, skew = Vector2.Zero, anchorPoint = Vector2.Zero, rotationCenter = Vector2.Zero;
            Vector4 color = Vector4.One;
            int quad = element.Quad, anchor = element.Anchor;
            bool visible = true, changedQuad = false;
            if (timeline != null)
            {
                foreach (IGrouping<int, AnimationTrack> group in timeline.Tracks.GroupBy(t => t.Type))
                {
                    AnimationTrack track = new(group.Key, [.. group.SelectMany(t => t.Keys)]);
                    if (track.Type == 4)
                    {
                        double time = 0;
                        foreach (AnimationKey key in track.Keys)
                        {
                            time += key.Time;
                            if (time > seconds + 1e-6)
                            {
                                break;
                            }
                            switch (key.Action)
                            {
                                case 0:
                                    visible = key.P2 != 0;
                                    break;
                                case 1:
                                    quad = checked((int)key.P1);
                                    changedQuad = true;
                                    break;
                                case 2:
                                    anchor = 64;
                                    anchorPoint = new(key.P1, key.P2);
                                    break;
                                case 3:
                                    rotationCenter = new(key.P1, key.P2);
                                    break;
                                case 4:
                                    break;
                                default:
                                    throw new ArgumentException("Unsupported animation action.");
                            }
                        }
                        continue;
                    }
                    Vector4? sampled = SampleTrack(track, timeline.FrameTime, seconds);
                    if (sampled is not Vector4 value)
                    {
                        continue;
                    }
                    // Serialized IDs differ from DX TrackType: color=2 and skew=3, with no rotation track.
                    switch (track.Type)
                    {
                        case 0:
                            position = new(value.X, value.Y);
                            break;
                        case 1:
                            scale = new(value.X, value.Y);
                            break;
                        case 2:
                            color = value;
                            break;
                        case 3:
                            skew = new(value.X, value.Y);
                            break;
                        default:
                            throw new ArgumentException("Unsupported animation track.");
                    }
                }
            }
            if (!visible)
            {
                return;
            }
            Vector2 size = element.Type == 1 ? quadSize(element.Source, quad) : Vector2.Zero;
            if (!changedQuad)
            {
                size = new(element.Width ?? size.X, element.Height ?? size.Y);
            }
            if (element.ParentAnchor != -1)
            {
                position += parentPosition + AnchorOffset(element.ParentAnchor, parentSize);
            }
            position -= (anchor & 64) != 0 ? anchorPoint : AnchorOffset(anchor, size);
            Vector2 pivot = position + (size / 2) + rotationCenter;
            MatrixStack stack = new();
            stack.Translate(pivot.X, pivot.Y);
            stack.Skew(skew.X, skew.Y);
            stack.Scale(scale.X, scale.Y);
            stack.Translate(-pivot.X, -pivot.Y);
            Matrix4x4 transform = stack.ModelView * parentTransform;
            if (element.Type == 1)
            {
                sprites.Add(new(element.Source, quad, Matrix4x4.CreateTranslation(position.X, position.Y, 0) * transform, color * parentColor));
            }
            foreach (AnimationElement child in element.Children)
            {
                Visit(child, scene, seconds, quadSize, position, size, transform, color * parentColor, sprites, depth + 1);
            }
        }

        /// <summary>Returns the standard anchor offset within a rectangle.</summary>
        /// <param name="anchor">Anchor bitmask.</param>
        /// <param name="size">Logical dimensions.</param>
        /// <returns>The horizontal and vertical anchor offsets.</returns>
        private static Vector2 AnchorOffset(int anchor, Vector2 size)
        {
            return new((anchor & 1) != 0 ? 0 : (anchor & 2) != 0 ? size.X / 2 : (anchor & 4) != 0 ? size.X : 0,
                (anchor & 8) != 0 ? 0 : (anchor & 16) != 0 ? size.Y / 2 : (anchor & 32) != 0 ? size.Y : 0);
        }

        /// <summary>Interpolates property values with destination easing and per-segment frame quantization.</summary>
        /// <param name="track">Property track.</param>
        /// <param name="frameTime">Authored frame duration.</param>
        /// <param name="seconds">Scene time.</param>
        /// <returns>The sampled value, or null before the first key.</returns>
        private static Vector4? SampleTrack(AnimationTrack track, float frameTime, double seconds)
        {
            if (track.Keys.Count == 0 || seconds + 1e-6 < track.Keys[0].Time)
            {
                return null;
            }
            double start = track.Keys[0].Time;
            AnimationKey previous = track.Keys[0];
            foreach (AnimationKey next in track.Keys.Skip(1))
            {
                double end = start + next.Time;
                if (seconds + 1e-6 < end)
                {
                    double elapsed = seconds - start;
                    if (frameTime > 0)
                    {
                        elapsed = Math.Floor(elapsed / frameTime) * frameTime;
                    }
                    float progress = (float)Math.Clamp(elapsed / next.Time, 0, 1);
                    return Vector4.Lerp(Value(previous), Value(next), Easing(progress, next.Transition));
                }
                start = end;
                previous = next;
            }
            return Value(previous);
        }

        /// <summary>Converts keyframe property components to a vector.</summary>
        /// <param name="key">Property keyframe.</param>
        /// <returns>Its four components.</returns>
        private static Vector4 Value(AnimationKey key)
        {
            return new(key.P1, key.P2, key.P3, key.P4);
        }

        /// <summary>Evaluates the Flash easing curves adapted from DX's Track interpolation.</summary>
        /// <param name="progress">Normalized segment progress.</param>
        /// <param name="transition">Original serialized transition ID.</param>
        /// <returns>The eased progress.</returns>
        private static float Easing(float progress, int transition)
        {
            float doubled = progress + progress;
            float shifted = doubled - 1;
            return transition switch
            {
                0 => progress,
                1 => 1,
                2 => progress * progress,
                3 => 1 - ((1 - progress) * (1 - progress)),
                4 => doubled < 1 ? .5f * doubled * doubled : -.5f * (((doubled - 2) * (doubled - 2)) - 2),
                5 => doubled < 1 ? .5f * (1 - (shifted * shifted)) : .5f * ((shifted * shifted) + 1),
                6 => 0,
                _ => throw new ArgumentException("Unsupported animation transition.")
            };
        }
    }
}
