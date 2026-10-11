using System;
using System.IO;

using Ctr2.Assets;
using Ctr2.Engine;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises synthetic animation bytes and deterministic source timeline contracts.</summary>
    public sealed class AnimationTests
    {
        /// <summary>Verifies nested elements, defaults, and ignored unknown fields.</summary>
        [Fact]
        public void ReadsTreeAndDefaults()
        {
            AnimationElement root = AnimationReader.Read([8, 2, 74, 4, 8, 1, 24, 7, 160, 6, 1]);
            Assert.Equal(2, root.Type);
            Assert.Equal(7, Assert.Single(root.Children).Source);
            Assert.Equal(9, root.Anchor);
            Assert.Empty(root.Timelines);
        }

        /// <summary>Verifies broken wire data is rejected with a bounded diagnostic.</summary>
        /// <param name="hex">Malformed protobuf bytes.</param>
        [Theory]
        [InlineData("4A05")]
        [InlineData("00")]
        [InlineData("0803")]
        [InlineData("0D00000000")]
        [InlineData("3D0000807F")]
        [InlineData("088080808080808080808002")]
        public void RejectsMalformedData(string hex)
        {
            _ = Assert.Throws<InvalidDataException>(() => AnimationReader.Read(Convert.FromHexString(hex)));
        }

        /// <summary>Verifies excessive recursive nesting is stopped before exhausting the process stack.</summary>
        [Fact]
        public void RejectsDeepTrees()
        {
            byte[] message = [];
            for (int i = 0; i < 80; i++)
            {
                using MemoryStream stream = new();
                stream.WriteByte(74);
                int length = message.Length;
                while (length > 127)
                {
                    stream.WriteByte((byte)((length & 127) | 128));
                    length >>= 7;
                }
                stream.WriteByte((byte)length);
                stream.Write(message);
                message = stream.ToArray();
            }
            _ = Assert.Throws<InvalidDataException>(() => AnimationReader.Read(message));
        }

        /// <summary>Verifies actions sharing a timestamp all survive and use param2 for visibility.</summary>
        [Fact]
        public void AppliesSimultaneousActionsAndTrimmedSize()
        {
            AnimationElement child = Element(1, [new(4, [new(0, 0, 1, 0, 0, 0, 1), new(0, 2, 0, 0, 0, 1, 1), new(0, 3, 4, 0, 0, 2, 1)])]);
            AnimationElement root = Root(child);
            AnimationSprite sprite = Assert.Single(AnimationPose.Sample(root, 0, 0, (_, _) => new(10, 20)));
            Assert.Equal(2, sprite.Quad);
            Assert.Equal(-3, sprite.Transform.M41);
            Assert.Equal(-4, sprite.Transform.M42);
        }

        /// <summary>Verifies serialized color and skew IDs do not alias the DX rotation track.</summary>
        [Fact]
        public void MapsTracksAndQuantizesWithinSegment()
        {
            AnimationTrack position = new(0, [new(0, 10, 0, 0, 0, 0, 1), new(1, 30, 0, 0, 0, 0, 2)]);
            AnimationElement child = Element(1, [position, new(2, [new(0, .2f, .3f, .4f, .5f, 0, 1)]), new(3, [new(0, 90, 0, 0, 0, 0, 1)])], .25f);
            AnimationSprite sprite = Assert.Single(AnimationPose.Sample(Root(child), 0, .49, (_, _) => new(0, 0)));
            Assert.Equal(11.25f, sprite.Transform.M41, 4);
            Assert.Equal(.5f, sprite.Color.W);
            Assert.Equal(-1, sprite.Transform.M21, 4);
        }

        /// <summary>Verifies a scene's final pose holds, and empty scene parts remain hidden.</summary>
        [Fact]
        public void HoldsTerminalPoseAndHidesEmptyScene()
        {
            AnimationElement child = Element(1, [new(0, [new(0, 10, 0, 0, 0, 0, 1), new(1, 30, 0, 0, 0, 0, 0)])]);
            Assert.Equal(30, Assert.Single(AnimationPose.Sample(Root(child), 0, 100, (_, _) => new(0, 0))).Transform.M41);
            AnimationElement empty = child with
            {
                Timelines = [new(0, "", true, 0, [])]
            };
            Assert.Empty(AnimationPose.Sample(Root(empty), 0, 0, (_, _) => new(0, 0)));
        }

        /// <summary>Verifies inherited parent tint multiplies child color and retains nested transforms.</summary>
        [Fact]
        public void InheritsTintAndParentTransform()
        {
            AnimationElement child = Element(1, [new(0, [new(0, 2, 3, 0, 0, 0, 1)])]);
            AnimationElement root = Root(child) with
            {
                Timelines = [new(0, "IDLE", false, 0,
                [new(1, [new(0, 2, 2, 0, 0, 0, 1)]), new(2, [new(0, .5f, 1, 1, .25f, 0, 1)])])]
            };
            AnimationSprite sprite = Assert.Single(AnimationPose.Sample(root, 0, 0, (_, _) => new(4, 4)));
            Assert.Equal(.25f, sprite.Color.W);
            Assert.Equal(.5f, sprite.Color.X);
            Assert.Equal(-46, sprite.Transform.M41);
            Assert.Equal(-44, sprite.Transform.M42);
        }

        /// <summary>Verifies repeated serialized tracks append to one runtime track, as in shipped UI and fireball scenes.</summary>
        [Fact]
        public void AppendsRepeatedPropertyTracks()
        {
            AnimationElement parsed = AnimationReader.Read([82, 8, 42, 2, 8, 0, 42, 2, 8, 0]);
            Assert.Equal(2, Assert.Single(parsed.Timelines).Tracks.Count);
            AnimationElement child = Element(1, [new(0, [new(0, 10, 0, 0, 0, 0, 1), new(1, 20, 0, 0, 0, 0, 0)]),
                new(0, [new(0, 30, 0, 0, 0, 0, 1), new(1, 40, 0, 0, 0, 0, 0)])]);
            Assert.Equal(15, Assert.Single(AnimationPose.Sample(Root(child), 0, .5, (_, _) => new(0, 0))).Transform.M41);
            Assert.Equal(35, Assert.Single(AnimationPose.Sample(Root(child), 0, 1.5, (_, _) => new(0, 0))).Transform.M41);
            Assert.Equal(2, AnimationPose.Duration(Root(child), 0));
        }

        /// <summary>Creates an image part with one synthetic scene.</summary>
        /// <param name="type">Element type.</param>
        /// <param name="tracks">Scene tracks.</param>
        /// <param name="frameTime">Authored frame duration.</param>
        /// <returns>The generated part.</returns>
        private static AnimationElement Element(int type, AnimationTrack[] tracks, float frameTime = 0)
        {
            return new(type, "part", 7, 0, 64, 9, null, null, [], [new(0, "", false, frameTime, tracks)]);
        }

        /// <summary>Wraps a part in a named scene root.</summary>
        /// <param name="child">Part to attach.</param>
        /// <returns>The scene root.</returns>
        private static AnimationElement Root(AnimationElement child)
        {
            return new(2, "root", 0, 0, 9, 9, 100, 100, [child], [new(0, "IDLE", false, 0, [])]);
        }
    }
}
