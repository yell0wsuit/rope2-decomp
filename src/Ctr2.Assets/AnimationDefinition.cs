using System.Collections.Generic;

namespace Ctr2.Assets
{
    /// <summary>One serialized element; child order is the authored drawing order.</summary>
    /// <param name="Type">Serialized kind: zero container, one image, two Flash animation.</param>
    /// <param name="Name">Optional authored name, or an empty string.</param>
    /// <param name="Source">Original image resource ID.</param>
    /// <param name="Quad">Initial zero-based image quad.</param>
    /// <param name="Anchor">Element anchor bits; 64 selects a custom point.</param>
    /// <param name="ParentAnchor">Parent anchor bits, or -1 for absolute positioning.</param>
    /// <param name="Width">Explicit logical width, or null to use the selected quad width.</param>
    /// <param name="Height">Explicit logical height, or null to use the selected quad height.</param>
    /// <param name="Children">Ordered child elements.</param>
    /// <param name="Timelines">Scenes keyed by original timeline ID.</param>
    public sealed record AnimationElement(int Type, string Name, int Source, int Quad, int Anchor, int ParentAnchor,
        float? Width, float? Height, IReadOnlyList<AnimationElement> Children, IReadOnlyList<AnimationTimeline> Timelines);

    /// <summary>One element's authored scene.</summary>
    /// <param name="Id">Original scene ID.</param>
    /// <param name="Name">Optional scene name.</param>
    /// <param name="IsEmpty">True when the element is absent from this scene.</param>
    /// <param name="FrameTime">Interpolation frame duration in seconds; zero allows continuous evaluation.</param>
    /// <param name="Tracks">Serialized property tracks.</param>
    public sealed record AnimationTimeline(int Id, string Name, bool IsEmpty, float FrameTime, IReadOnlyList<AnimationTrack> Tracks);

    /// <summary>A property track using serialized IDs rather than DX runtime enum values.</summary>
    /// <param name="Type">Zero position, one scale, two color, three skew, four action.</param>
    /// <param name="Keys">Ordered delta-time keys; equal-time actions remain separate.</param>
    public sealed record AnimationTrack(int Type, IReadOnlyList<AnimationKey> Keys);

    /// <summary>A delta-time property value or action.</summary>
    /// <param name="Time">Nonnegative seconds since the preceding key.</param>
    /// <param name="P1">First property component or action argument.</param>
    /// <param name="P2">Second property component or action argument.</param>
    /// <param name="P3">Third color component.</param>
    /// <param name="P4">Fourth color component.</param>
    /// <param name="Action">Zero visibility, one quad, two anchor, three rotation center, four delay.</param>
    /// <param name="Transition">Zero linear, one immediate, two ease in, three ease out, four in/out, five out/in, six hold.</param>
    public sealed record AnimationKey(float Time, float P1, float P2, float P3, float P4, int Action, int Transition);
}
