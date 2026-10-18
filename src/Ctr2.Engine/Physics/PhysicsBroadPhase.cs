// Altered C# port of the Box2D 2.2.1 dynamic tree and broadphase by Erin Catto.
// Uses the audited Android numerical profile. See PHYSICS.md for scope.
/*
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org
*
* This software is provided 'as-is', without any express or implied
* warranty. In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>An axis-aligned box in world meters; touching boundaries overlap.</summary>
    /// <param name="Lower">Minimum coordinates.</param>
    /// <param name="Upper">Maximum coordinates.</param>
    public readonly record struct PhysicsAabb(Vector2 Lower, Vector2 Upper)
    {
        internal float Perimeter => 2 * (Upper.X - Lower.X + (Upper.Y - Lower.Y));
        internal bool Contains(PhysicsAabb other)
        {
            return Lower.X <= other.Lower.X && Lower.Y <= other.Lower.Y &&
            other.Upper.X <= Upper.X && other.Upper.Y <= Upper.Y;
        }

        internal bool Overlaps(PhysicsAabb other)
        {
            return !(other.Lower.X - Upper.X > 0 || other.Lower.Y - Upper.Y > 0 ||
                    Lower.X - other.Upper.X > 0 || Lower.Y - other.Upper.Y > 0);
        }

        internal static PhysicsAabb Combine(PhysicsAabb a, PhysicsAabb b)
        {
            return new(Vector2.Min(a.Lower, b.Lower), Vector2.Max(a.Upper, b.Upper));
        }

        internal void Validate()
        {
            if (!ValidVector(Lower) || !ValidVector(Upper) || Lower.X > Upper.X || Lower.Y > Upper.Y)
            {
                throw new ArgumentOutOfRangeException(nameof(Lower), "Invalid finite broadphase bounds.");
            }
        }
        internal static bool ValidVector(Vector2 point)
        {
            return float.IsFinite(point.X) && float.IsFinite(point.Y) &&
            MathF.Abs(point.X) <= 1000000 && MathF.Abs(point.Y) <= 1000000;
        }
    }

    /// <summary>A candidate's user-data values, ordered by canonical proxy IDs rather than data values.</summary>
    /// <param name="First">User data of the lower proxy ID.</param>
    /// <param name="Second">User data of the higher proxy ID.</param>
    public readonly record struct BroadPhasePair(int First, int Second);

    /// <summary>Headless native-style proxy tree and candidate generation, separate from world contact ownership.</summary>
    public sealed class PhysicsBroadPhase
    {
        private const int MaximumProxies = 16384;
        private const int MaximumPendingMoves = 65536;
        private const int MaximumCandidatePairs = 1000000;
        private readonly Tree tree = new();
        private readonly List<int> moves = [];

        /// <summary>Gets whether a query or pair update is in progress.</summary>
        internal bool Locked
        {
            get; private set;
        }

        /// <summary>Gets the number of live leaf proxies.</summary>
        public int ProxyCount
        {
            get; private set;
        }
        /// <summary>Gets the balanced tree height; an empty tree and a single leaf have height zero.</summary>
        public int Height => tree.Height;

        /// <summary>Creates a proxy with zero fixed padding and returns its reusable native-style node ID.</summary>
        public int CreateProxy(PhysicsAabb bounds, int userData)
        {
            CheckMutation();
            bounds.Validate();
            if (ProxyCount >= MaximumProxies)
            {
                throw new InvalidOperationException("Broadphase proxy budget exceeded.");
            }
            CheckMoveCapacity();
            int id = tree.Create(bounds, userData);
            ProxyCount++;
            moves.Add(id);
            return id;
        }

        /// <summary>Destroys a live proxy, invalidating the first matching move entry as native Box2D does.</summary>
        public void DestroyProxy(int proxyId)
        {
            CheckMutation();
            _ = tree.Leaf(proxyId);
            int pending = moves.IndexOf(proxyId);
            if (pending >= 0)
            {
                moves[pending] = -1;
            }
            tree.Destroy(proxyId);
            ProxyCount--;
        }

        /// <summary>Moves an owned proxy and predicts bounds using displacement times one; returns whether reinserted.</summary>
        public bool MoveProxy(int proxyId, PhysicsAabb bounds, Vector2 displacement)
        {
            CheckMutation();
            _ = tree.Leaf(proxyId);
            bounds.Validate();
            if (!PhysicsAabb.ValidVector(displacement))
            {
                throw new ArgumentOutOfRangeException(nameof(displacement));
            }
            if (tree.Leaf(proxyId).Bounds.Contains(bounds))
            {
                return false;
            }
            CheckMoveCapacity();
            if (!tree.Move(proxyId, bounds, displacement))
            {
                return false;
            }
            moves.Add(proxyId);
            return true;
        }

        /// <summary>Queues another query for a live proxy; duplicate pending moves are preserved.</summary>
        public void TouchProxy(int proxyId)
        {
            CheckMutation();
            _ = tree.Leaf(proxyId);
            CheckMoveCapacity();
            moves.Add(proxyId);
        }

        /// <summary>Queues world refilter discovery without duplicating an already pending leaf.</summary>
        internal void TouchProxyOnce(int proxyId)
        {
            CheckMutation();
            _ = tree.Leaf(proxyId);
            if (!moves.Contains(proxyId))
            {
                TouchProxy(proxyId);
            }
        }

        /// <summary>Gets the stored predicted box of a live proxy.</summary>
        public PhysicsAabb GetBounds(int proxyId)
        {
            return tree.Leaf(proxyId).Bounds;
        }

        /// <summary>Gets the integer user data of a live proxy.</summary>
        public int GetUserData(int proxyId)
        {
            return tree.Leaf(proxyId).Data;
        }

        /// <summary>Snapshots overlapping proxy IDs in native LIFO tree traversal order.</summary>
        public IReadOnlyList<int> Query(PhysicsAabb bounds)
        {
            List<int> result = [];
            Query(bounds, id => { result.Add(id); return true; });
            return result.AsReadOnly();
        }

        /// <summary>Queries overlapping proxies until the callback returns false. Callbacks must not mutate the tree.</summary>
        public void Query(PhysicsAabb bounds, Func<int, bool> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            bounds.Validate();
            bool previous = Locked;
            Locked = true;
            try
            {
                tree.Query(bounds, callback);
            }
            finally { Locked = previous; }
        }

        /// <summary>Casts a segment through proxy bounds in native tree order. Callbacks must not mutate the tree.</summary>
        /// <param name="point1">Segment start.</param>
        /// <param name="point2">Segment end, distinct from the start.</param>
        /// <param name="callback">Receives the current maximum fraction and a leaf proxy ID. Returning zero stops,
        /// a positive value clips the segment to that fraction, and a negative value leaves it unchanged.</param>
        public void RayCast(Vector2 point1, Vector2 point2, Func<float, int, float> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            if (!PhysicsAabb.ValidVector(point1) || !PhysicsAabb.ValidVector(point2) || point1 == point2)
            {
                throw new ArgumentOutOfRangeException(nameof(point2));
            }
            bool previous = Locked;
            Locked = true;
            try
            {
                tree.RayCast(point1, point2, callback);
            }
            finally { Locked = previous; }
        }

        /// <summary>Consumes moved proxies and returns unique candidate data in lexicographic proxy-ID order.</summary>
        /// <remarks>These are new candidate reports, not a persistent contact set. Retire IDs immediately after destruction.</remarks>
        public IReadOnlyList<BroadPhasePair> UpdatePairs()
        {
            CheckMutation();
            // Native code can leave a stale move after duplicate touches followed by destruction.
            // Reject a freed pending ID before consuming the buffer; a reused live ID follows native aliasing.
            foreach (int id in moves)
            {
                if (id != -1)
                {
                    _ = tree.Leaf(id);
                }
            }
            Locked = true;
            try
            {
                List<(int A, int B)> pairs = [];
                foreach (int id in moves)
                {
                    if (id == -1)
                    {
                        continue;
                    }
                    tree.Query(tree.Leaf(id).Bounds, other =>
                    {
                        if (other != id)
                        {
                            if (pairs.Count >= MaximumCandidatePairs)
                            {
                                throw new InvalidOperationException("Broadphase candidate-pair budget exceeded.");
                            }
                            pairs.Add((Math.Min(id, other), Math.Max(id, other)));
                        }
                        return true;
                    });
                }
                pairs.Sort((a, b) => a.A != b.A ? a.A.CompareTo(b.A) : a.B.CompareTo(b.B));
                List<BroadPhasePair> result = [];
                (int A, int B)? previous = null;
                foreach ((int A, int B) pair in pairs)
                {
                    if (previous != pair)
                    {
                        result.Add(new(tree.Leaf(pair.A).Data, tree.Leaf(pair.B).Data));
                        previous = pair;
                    }
                }
                moves.Clear();
                return result.AsReadOnly();
            }
            finally { Locked = false; }
        }

        private void CheckMutation()
        {
            if (Locked)
            {
                throw new InvalidOperationException("Broadphase mutation during a query is not supported.");
            }
        }

        private void CheckMoveCapacity()
        {
            if (moves.Count >= MaximumPendingMoves)
            {
                throw new InvalidOperationException("Broadphase pending-move budget exceeded.");
            }
        }

        private sealed class Node
        {
            internal PhysicsAabb Bounds;
            internal int Data;
            internal int Parent = -1;
            internal int Child1 = -1;
            internal int Child2 = -1;
            internal int Height = -1;
        }

        private sealed class Tree
        {
            private Node[] nodes = new Node[16];
            private int root = -1;
            private int free;
            internal int Height => root == -1 ? 0 : nodes[root].Height;
            internal Tree()
            {
                InitializeFree(0);
            }

            private void InitializeFree(int start)
            {
                for (int i = start; i < nodes.Length; i++)
                {
                    nodes[i] = new()
                    {
                        Parent = i + 1
                    };
                }
                nodes[^1].Parent = -1;
                free = start;
            }

            private int Allocate()
            {
                if (free == -1)
                {
                    int size = nodes.Length;
                    Array.Resize(ref nodes, 2 * size);
                    InitializeFree(size);
                }
                int id = free;
                Node node = nodes[id];
                free = node.Parent;
                node.Parent = node.Child1 = node.Child2 = -1;
                node.Height = 0;
                node.Data = 0;
                return id;
            }

            private void Free(int id)
            {
                nodes[id].Parent = free;
                nodes[id].Height = -1;
                free = id;
            }

            internal Node Leaf(int id)
            {
                return (uint)id >= nodes.Length || nodes[id].Height < 0 || nodes[id].Child1 != -1
                    ? throw new ArgumentOutOfRangeException(nameof(id), "Expected a live broadphase leaf.")
                    : nodes[id];
            }

            internal int Create(PhysicsAabb bounds, int data)
            {
                int id = Allocate();
                nodes[id].Bounds = bounds;
                nodes[id].Data = data;
                Insert(id);
                return id;
            }

            internal void Destroy(int id)
            {
                Remove(id);
                Free(id);
            }

            internal bool Move(int id, PhysicsAabb bounds, Vector2 displacement)
            {
                if (nodes[id].Bounds.Contains(bounds))
                {
                    return false;
                }
                Remove(id);
                Vector2 lower = bounds.Lower, upper = bounds.Upper;
                if (displacement.X < 0)
                {
                    lower.X += displacement.X;
                }
                else
                {
                    upper.X += displacement.X;
                }
                if (displacement.Y < 0)
                {
                    lower.Y += displacement.Y;
                }
                else
                {
                    upper.Y += displacement.Y;
                }
                nodes[id].Bounds = new(lower, upper);
                Insert(id);
                return true;
            }

            /// <summary>Native <c>b2DynamicTree::RayCast</c>: segment box and separating-axis culling, LIFO traversal.</summary>
            internal void RayCast(Vector2 p1, Vector2 p2, Func<float, int, float> callback)
            {
                Vector2 r = PhysicsMath.Normalize(p2 - p1);
                Vector2 v = new(-r.Y, r.X), absV = new(MathF.Abs(v.X), MathF.Abs(v.Y));
                float maxFraction = 1;
                PhysicsAabb segment = Segment(p1, p2, maxFraction);
                Stack<int> stack = new();
                stack.Push(root);
                while (stack.TryPop(out int id))
                {
                    if (id == -1 || !nodes[id].Bounds.Overlaps(segment))
                    {
                        continue;
                    }
                    Node node = nodes[id];
                    Vector2 center = .5f * (node.Bounds.Lower + node.Bounds.Upper), extents = .5f * (node.Bounds.Upper - node.Bounds.Lower);
                    Vector2 offset = p1 - center;
                    float separation = MathF.Abs((v.X * offset.X) + (v.Y * offset.Y)) - ((absV.X * extents.X) + (absV.Y * extents.Y));
                    if (separation > 0)
                    {
                        continue;
                    }
                    if (node.Child1 == -1)
                    {
                        float value = callback(maxFraction, id);
                        if (value == 0)
                        {
                            return;
                        }
                        if (value > 0)
                        {
                            maxFraction = value;
                            segment = Segment(p1, p2, maxFraction);
                        }
                    }
                    else
                    {
                        stack.Push(node.Child1);
                        stack.Push(node.Child2);
                    }
                }
            }

            private static PhysicsAabb Segment(Vector2 p1, Vector2 p2, float fraction)
            {
                Vector2 t = p1 + (fraction * (p2 - p1));
                return new(new(MathF.Min(p1.X, t.X), MathF.Min(p1.Y, t.Y)), new(MathF.Max(p1.X, t.X), MathF.Max(p1.Y, t.Y)));
            }

            internal void Query(PhysicsAabb bounds, Func<int, bool> callback)
            {
                Stack<int> stack = new();
                stack.Push(root);
                while (stack.TryPop(out int id))
                {
                    if (id == -1 || !nodes[id].Bounds.Overlaps(bounds))
                    {
                        continue;
                    }
                    Node node = nodes[id];
                    if (node.Child1 == -1)
                    {
                        if (!callback(id))
                        {
                            return;
                        }
                    }
                    else
                    {
                        stack.Push(node.Child1);
                        stack.Push(node.Child2);
                    }
                }
            }

            private void Insert(int leaf)
            {
                if (root == -1)
                {
                    root = leaf;
                    nodes[leaf].Parent = -1;
                    return;
                }
                PhysicsAabb box = nodes[leaf].Bounds;
                int index = root;
                while (nodes[index].Child1 != -1)
                {
                    Node node = nodes[index];
                    float area = node.Bounds.Perimeter;
                    float combined = PhysicsAabb.Combine(node.Bounds, box).Perimeter;
                    float cost = 2 * combined;
                    float inheritance = 2 * (combined - area);
                    float cost1 = DescendCost(node.Child1, box, inheritance);
                    float cost2 = DescendCost(node.Child2, box, inheritance);
                    if (cost < cost1 && cost < cost2)
                    {
                        break;
                    }
                    index = cost1 < cost2 ? node.Child1 : node.Child2;
                }
                int sibling = index;
                int oldParent = nodes[sibling].Parent;
                int parent = Allocate();
                Node p = nodes[parent];
                p.Parent = oldParent;
                p.Bounds = PhysicsAabb.Combine(box, nodes[sibling].Bounds);
                p.Height = nodes[sibling].Height + 1;
                p.Child1 = sibling;
                p.Child2 = leaf;
                ReplaceChild(oldParent, sibling, parent);
                nodes[sibling].Parent = parent;
                nodes[leaf].Parent = parent;
                Refit(parent);
            }

            private float DescendCost(int child, PhysicsAabb box, float inheritance)
            {
                Node node = nodes[child];
                float combined = PhysicsAabb.Combine(box, node.Bounds).Perimeter;
                return node.Child1 == -1 ? combined + inheritance : combined - node.Bounds.Perimeter + inheritance;
            }

            private void Remove(int leaf)
            {
                if (leaf == root)
                {
                    root = -1;
                    return;
                }
                int parent = nodes[leaf].Parent;
                int grand = nodes[parent].Parent;
                int sibling = nodes[parent].Child1 == leaf ? nodes[parent].Child2 : nodes[parent].Child1;
                ReplaceChild(grand, parent, sibling);
                nodes[sibling].Parent = grand;
                Free(parent);
                if (grand != -1)
                {
                    Refit(grand);
                }
            }

            private void ReplaceChild(int parent, int oldChild, int newChild)
            {
                if (parent == -1)
                {
                    root = newChild;
                }
                else if (nodes[parent].Child1 == oldChild)
                {
                    nodes[parent].Child1 = newChild;
                }
                else
                {
                    nodes[parent].Child2 = newChild;
                }
            }

            private void Refit(int index)
            {
                while (index != -1)
                {
                    index = Balance(index);
                    Node node = nodes[index];
                    Node first = nodes[node.Child1], second = nodes[node.Child2];
                    node.Bounds = PhysicsAabb.Combine(first.Bounds, second.Bounds);
                    node.Height = 1 + Math.Max(first.Height, second.Height);
                    index = node.Parent;
                }
            }

            private int Balance(int index)
            {
                Node a = nodes[index];
                if (a.Child1 == -1 || a.Height < 2)
                {
                    return index;
                }
                int left = a.Child1, right = a.Child2;
                Node b = nodes[left], c = nodes[right];
                int difference = c.Height - b.Height;
                if (difference > 1)
                {
                    int f = c.Child1, g = c.Child2;
                    c.Child1 = index;
                    c.Parent = a.Parent;
                    a.Parent = right;
                    ReplaceChild(c.Parent, index, right);
                    if (nodes[f].Height > nodes[g].Height)
                    {
                        c.Child2 = f;
                        a.Child2 = g;
                        nodes[g].Parent = index;
                    }
                    else
                    {
                        c.Child2 = g;
                        a.Child2 = f;
                        nodes[f].Parent = index;
                    }
                    UpdateNode(index);
                    UpdateNode(right);
                    return right;
                }
                if (difference < -1)
                {
                    int d = b.Child1, e = b.Child2;
                    b.Child1 = index;
                    b.Parent = a.Parent;
                    a.Parent = left;
                    ReplaceChild(b.Parent, index, left);
                    if (nodes[d].Height > nodes[e].Height)
                    {
                        b.Child2 = d;
                        a.Child1 = e;
                        nodes[e].Parent = index;
                    }
                    else
                    {
                        b.Child2 = e;
                        a.Child1 = d;
                        nodes[d].Parent = index;
                    }
                    UpdateNode(index);
                    UpdateNode(left);
                    return left;
                }
                return index;
            }

            private void UpdateNode(int id)
            {
                Node node = nodes[id];
                node.Bounds = PhysicsAabb.Combine(nodes[node.Child1].Bounds, nodes[node.Child2].Bounds);
                node.Height = 1 + Math.Max(nodes[node.Child1].Height, nodes[node.Child2].Height);
            }
        }
    }
}
