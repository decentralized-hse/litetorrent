﻿using System.Collections;

namespace LiteTorrent.Domain;

public record Action
(
    int TreeIndex,
    int IndexInTree,
    Hash Hash
);

public class MerkleTree
{
    private readonly List<Hash[]> trees = new();
    private readonly Hash[] rootTree;
    private readonly List<int> leafCounts = new();
    private readonly Hash[] pieces;
    
    public Hash RootHash { get; private set; }

    private readonly Queue<Action> addQueue = new();
    
    private MerkleTree(int count)
    {
        pieces = Hash.CreateArray(count);
        
        while (count != 0)
        {
            var leafCount = (int)Math.Pow(2, (int)Math.Log2(count));
            leafCounts.Add(leafCount);
            trees.Add(Hash.CreateArray(2 * leafCount - 1));
            count -= leafCount;
        }

        rootTree = Hash.CreateArray(trees.Count * 2 - 1);
    }

    public MerkleTree(int count, Hash rootHash) : this(count)
    {
        RootHash = rootHash;
    }

    public MerkleTree(List<Hash[]> trees, Hash[] rootTree, Hash rootHash, Hash[] pieces)
    {
        RootHash = rootHash;
        this.trees = trees;
        this.rootTree = rootTree;
        this.pieces = pieces;
        leafCounts = CalculateLeafCounts(pieces.Length);
    }

    public MerkleTree(Hash[] pieces) : this(pieces.Length)
    {
        this.pieces = pieces.ToArray();
        leafCounts = CalculateLeafCounts(pieces.Length);
        BuildAllTree(pieces);
    }

    public int PieceCount => pieces.Length;

    public (List<Hash[]> Trees, Hash[] RootTree, Hash RootHash, Hash[] Pieces) GetInnerData()
    {
        return (trees, rootTree, RootHash, pieces);
    }

    public BitArray GetLeafStates()
    {
        var bitArray = new BitArray(pieces.Length);
        for (var i = 0; i < pieces.Length; i++)
            bitArray.Set(i, !pieces[i].IsEmpty);

        return bitArray;
    }

    public bool TryAdd(int index, Hash itemHash, Hash[] path)
    {
        var (leafIndex, treeIndex) = GetIndexes(index);

        var currIndex = leafIndex + trees[treeIndex].Length - leafCounts[treeIndex];
        var rootHash = ComputeTreeHash(itemHash, treeIndex, currIndex, 0, path);

        if (rootHash != RootHash)
            return false;
        
        while (addQueue.Count != 0)
        {
            var act = addQueue.Dequeue();
            if (act.TreeIndex == -1)
                rootTree[act.IndexInTree] = act.Hash;
            else trees[act.TreeIndex][act.IndexInTree] = act.Hash;
        }

        pieces[index] = itemHash;

        return true;
    }
    
    public IEnumerable<Hash> GetPath(int index)
    {
        var (leafIndex, treeIndex) = GetIndexes(index);
        var currIndex = leafIndex + trees[treeIndex].Length - leafCounts[treeIndex];
        var itemHash = trees[treeIndex][currIndex];
        return GetTreePath(itemHash, treeIndex, currIndex);
    }

    public Hash GetPieceHash(int index)
    {
        return pieces[index];
    }

    private static List<int> CalculateLeafCounts(int pieceCount)
    {
        var leafCounts = new List<int>();
        while (pieceCount != 0)
        {
            var leafCount = (int)Math.Pow(2, (int)Math.Log2(pieceCount));
            leafCounts.Add(leafCount);
            pieceCount -= leafCount;
        }

        return leafCounts;
    }

    private (int leafIndex, int treeIndex) GetIndexes(int index)
    {
        var leafIndex = index;
        var treeIndex = 0;
        for (; leafIndex >= leafCounts[treeIndex]; ++treeIndex)
        {
            leafIndex -= leafCounts[treeIndex];
        }

        return (leafIndex, treeIndex);
    }
     
    private Hash ComputeTreeHash(Hash lastHash, int arrayIndex, int index, int pathIndex, Hash[] path)
    {
        while (true)
        {
            addQueue.Enqueue(new Action(arrayIndex, index, lastHash));
            if (index == 0)
            {
                var treeIndex = arrayIndex * 2 + ((arrayIndex * 2) == rootTree.Length - 1 ? 0 : 1);
                return ComputeRootHash(lastHash, treeIndex, pathIndex, path);
            }

            if (index % 2 == 0)
            {
                addQueue.Enqueue(new Action(arrayIndex, index - 1, path[pathIndex]));
                var left = path[pathIndex];
                lastHash = left.Concat(lastHash);
                index = (index - 1) / 2;
                pathIndex = ++pathIndex;
                continue;
            }

            addQueue.Enqueue(new Action(arrayIndex, index + 1, path[pathIndex]));
            var right = path[pathIndex];
            lastHash = lastHash.Concat(right);
            index = (index - 1) / 2;
            pathIndex = ++pathIndex;
        }
    }

    private Hash ComputeRootHash(Hash lastHash, int treeIndex, int pathIndex, Hash[] path)
    {
        while (true)
        {
            addQueue.Enqueue(new Action(-1, treeIndex, lastHash));
            if (treeIndex == 0)
            {
                return lastHash;
            }

            if (treeIndex % 2 == 0)
            {
                addQueue.Enqueue(new Action(-1, treeIndex - 1, path[pathIndex]));
                var left = path[pathIndex];
                lastHash = left.Concat(lastHash);
                treeIndex -= 2;
                pathIndex = ++pathIndex;
                continue;
            }

            addQueue.Enqueue(new Action(-1, treeIndex + 1, path[pathIndex]));
            var right = path[pathIndex];
            lastHash = lastHash.Concat(right);
            treeIndex -= 1;
            pathIndex = ++pathIndex;
        }
    }

    private IEnumerable<Hash> GetTreePath(Hash lastHash, int arrayIndex, int index)
    {
        while (true)
        {
            if (index == 0)
            {
                var treeIndex = arrayIndex * 2 + ((arrayIndex * 2) == rootTree.Length - 1 ? 0 : 1);
                foreach (var hash in GetRootPath(lastHash, treeIndex))
                {
                    yield return hash;
                }

                yield break;
            }

            if (index % 2 == 0)
            {
                var left = trees[arrayIndex][index - 1];
                yield return left;
                lastHash = left.Concat(lastHash);
                index = (index - 1) / 2;
                continue;
            }

            var right = trees[arrayIndex][index + 1];
            yield return right;
            lastHash = lastHash.Concat(right);
            index = (index - 1) / 2;
        }
    }

    private IEnumerable<Hash> GetRootPath(Hash lastHash, int treeIndex)
    {
        while (true)
        {
            if (treeIndex == 0)
            {
                yield break;
            }

            if (treeIndex % 2 == 0)
            {
                var left = rootTree[treeIndex - 1];
                yield return left;
                lastHash = left.Concat(lastHash);
                treeIndex -= 2;
                continue;
            }

            var right = rootTree[treeIndex + 1];
            yield return right;
            lastHash = lastHash.Concat(right);
            treeIndex -= 1;
        }
    }

    private Hash BuildAllTree(Hash[] pieces)
    {
        var pieceIndex = 0;
        for (var i = 0; i < trees.Count; i++)
        {
            var (leafIndex, treeIndex) = GetIndexes(pieceIndex);
            var curIndex = leafIndex + trees[treeIndex].Length - leafCounts[treeIndex];
            for (var j = curIndex; j < curIndex + leafCounts[i]; ++j)
            {
                trees[i][j] = pieces[pieceIndex++];
            }
        }
        RootHash = BuildTree(-1, 0);
        return RootHash;
    }

    private Hash BuildTree(int treeIndex, int index)
    {
        if (treeIndex == -1)
        {
            if (index % 2 != 0 || index == rootTree.Length - 1)
            {
                var calcHash =  BuildTree(index / 2, 0);
                rootTree[index] = calcHash;
                return calcHash;
            }
            var left = BuildTree(-1, index + 1);
            var right = BuildTree(-1, index + 2);
            var calcHash2 = left.Concat(right);
            rootTree[index] = calcHash2;
            return calcHash2;
        }
        var leafStart = trees[treeIndex].Length - leafCounts[treeIndex];
        if (index >= leafStart) {
            return trees[treeIndex][index];
        }
        var leftTreeHash = BuildTree(treeIndex, index * 2 + 1);
        var rightTreeHash = BuildTree(treeIndex, index * 2 + 2);
        var calcHash3 = leftTreeHash.Concat(rightTreeHash);
        trees[treeIndex][index] = calcHash3;
        return calcHash3;
    }
}