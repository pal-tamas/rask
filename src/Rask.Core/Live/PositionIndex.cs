using System.Buffers;

namespace Rask.Core.Live;

// An order-statistics list over the values 0..n-1 — a drop-in for the List&lt;int&gt; the keyed-reorder
// move loop uses (Add on build, then RankOf / RemoveAt / InsertAt), but with every operation in
// O(log n) instead of O(n). It turns the move loop from O(n²) to O(n log n) on large keyed reorders
// (a full 5000-row reversal drops from ~3 ms to tens of µs).
//
// Implemented as an order-statistics treap. The node id IS the value (values are a dense 0..n-1
// permutation), so left/right/parent/size/priority live in flat int[]s indexed by value, all rented
// from ArrayPool so a large reorder stays allocation-free (mirrors the ArrayPool rents already in the
// move step). Parent pointers make RankOf(value) an O(log n) walk to the root; treap priorities keep
// the tree balanced in expectation, so a worst-case (e.g. reversed) permutation can't degrade it back
// to O(n²). The emitted move positions are identical to the List path's, so both satisfy the same
// replay-to-target tests.
internal sealed class PositionIndex
{
    private const int Nil = -1;

    private int[] _left = [];
    private int[] _right = [];
    private int[] _parent = [];
    private int[] _size = [];
    private long[] _priority = [];
    private int _root = Nil;
    private bool _rented;

    // Deterministic PRNG for priorities (SplitMix64). Seeded to a constant: tree shape affects only
    // balance/perf, never the emitted positions, so reproducibility costs nothing and avoids flakiness.
    private ulong _rng;

    // Rents backing storage for capacity `n` and builds the sequence [0, 1, …, n-1].
    public void InitSequence(int n)
    {
        _left = ArrayPool<int>.Shared.Rent(n);
        _right = ArrayPool<int>.Shared.Rent(n);
        _parent = ArrayPool<int>.Shared.Rent(n);
        _size = ArrayPool<int>.Shared.Rent(n);
        _priority = ArrayPool<long>.Shared.Rent(n);
        _rented = true;
        _root = Nil;
        _rng = 0x9E3779B97F4A7C15UL;

        for (var i = 0; i < n; i++)
        {
            InsertAt(i, i); // append value i at position i
        }
    }

    public void Return()
    {
        if (!_rented)
        {
            return;
        }

        ArrayPool<int>.Shared.Return(_left);
        ArrayPool<int>.Shared.Return(_right);
        ArrayPool<int>.Shared.Return(_parent);
        ArrayPool<int>.Shared.Return(_size);
        ArrayPool<long>.Shared.Return(_priority);
        _left = _right = _parent = _size = [];
        _priority = [];
        _root = Nil;
        _rented = false;
    }

    public int Count => _root == Nil ? 0 : _size[_root];

    private int Sz(int node) => node == Nil ? 0 : _size[node];

    private void Update(int node) => _size[node] = 1 + Sz(_left[node]) + Sz(_right[node]);

    private long NextPriority()
    {
        // SplitMix64.
        _rng += 0x9E3779B97F4A7C15UL;
        var z = _rng;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (long)((z ^ (z >> 31)) >> 1); // non-negative
    }

    // 0-based position of `value` in the sequence — its in-order rank. O(log n) via the parent walk.
    public int RankOf(int value)
    {
        var rank = Sz(_left[value]);
        for (var cur = value; _parent[cur] != Nil; cur = _parent[cur])
        {
            if (_right[_parent[cur]] == cur)
            {
                rank += Sz(_left[_parent[cur]]) + 1;
            }
        }

        return rank;
    }

    // Inserts `value` so that it occupies position `index` (0..Count).
    public void InsertAt(int index, int value)
    {
        _left[value] = Nil;
        _right[value] = Nil;
        _size[value] = 1;
        _priority[value] = NextPriority();

        if (_root == Nil)
        {
            _parent[value] = Nil;
            _root = value;
            return;
        }

        var cur = _root;
        var parent = Nil;
        var goLeft = false;
        while (cur != Nil)
        {
            _size[cur]++; // value lands somewhere in cur's subtree
            parent = cur;
            var leftSize = Sz(_left[cur]);
            if (index <= leftSize)
            {
                goLeft = true;
                cur = _left[cur];
            }
            else
            {
                index -= leftSize + 1;
                goLeft = false;
                cur = _right[cur];
            }
        }

        _parent[value] = parent;
        if (goLeft)
        {
            _left[parent] = value;
        }
        else
        {
            _right[parent] = value;
        }

        // Restore the heap order on priorities by rotating `value` up.
        while (_parent[value] != Nil && _priority[value] > _priority[_parent[value]])
        {
            if (_left[_parent[value]] == value)
            {
                RotateRight(_parent[value]);
            }
            else
            {
                RotateLeft(_parent[value]);
            }
        }
    }

    // Removes and returns the value at position `index` (0..Count-1).
    public int RemoveAt(int index)
    {
        var cur = _root;
        while (true)
        {
            var leftSize = Sz(_left[cur]);
            if (index == leftSize)
            {
                break;
            }

            if (index < leftSize)
            {
                cur = _left[cur];
            }
            else
            {
                index -= leftSize + 1;
                cur = _right[cur];
            }
        }

        // Rotate `cur` down until it is a leaf (always pulling up the higher-priority child), then
        // detach it and drop the size by one along its ancestor chain.
        while (_left[cur] != Nil || _right[cur] != Nil)
        {
            if (_right[cur] == Nil ||
                (_left[cur] != Nil && _priority[_left[cur]] > _priority[_right[cur]]))
            {
                RotateRight(cur);
            }
            else
            {
                RotateLeft(cur);
            }
        }

        var parent = _parent[cur];
        if (parent == Nil)
        {
            _root = Nil;
        }
        else
        {
            if (_left[parent] == cur)
            {
                _left[parent] = Nil;
            }
            else
            {
                _right[parent] = Nil;
            }

            for (var a = parent; a != Nil; a = _parent[a])
            {
                _size[a]--;
            }
        }

        _parent[cur] = Nil;
        return cur;
    }

    // Right-rotate around x: its left child takes its place. Maintains parent links and subtree sizes.
    private void RotateRight(int x)
    {
        var y = _left[x];
        var b = _right[y];

        _left[x] = b;
        if (b != Nil)
        {
            _parent[b] = x;
        }

        Relink(x, y);
        _right[y] = x;
        _parent[x] = y;

        Update(x);
        Update(y);
    }

    // Left-rotate around x: its right child takes its place.
    private void RotateLeft(int x)
    {
        var y = _right[x];
        var b = _left[y];

        _right[x] = b;
        if (b != Nil)
        {
            _parent[b] = x;
        }

        Relink(x, y);
        _left[y] = x;
        _parent[x] = y;

        Update(x);
        Update(y);
    }

    // Points x's former parent (or the root) at y.
    private void Relink(int x, int y)
    {
        var px = _parent[x];
        _parent[y] = px;
        if (px == Nil)
        {
            _root = y;
        }
        else if (_left[px] == x)
        {
            _left[px] = y;
        }
        else
        {
            _right[px] = y;
        }
    }
}
