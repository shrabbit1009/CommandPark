using System.Security.Cryptography;
using System.Text;
using CommandPark.Utils.Clicks;
using JetBrains.Annotations;

namespace CommandPark.Tests.Utils.Clicks;

[TestSubject(typeof(HashToTimes))]
public class HashToTimesTest
{
    private static SHA256Store Hash(string value) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    [Fact]
    public void Add_NewKeys_AppearInInsertionOrder()
    {
        var htt = new HashToTimes();

        htt.Add(Hash("a"));
        htt.Add(Hash("b"));
        htt.Add(Hash("c"));

        Assert.Equal(
            new[] { Hash("a"), Hash("b"), Hash("c") },
            htt.Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public void Add_ExistingKey_IncrementsWithoutReordering()
    {
        var htt = new HashToTimes();
        htt.Add(Hash("a"));
        htt.Add(Hash("b"));

        Assert.Equal(2, htt.Add(Hash("a")));

        Assert.Equal(2, htt[Hash("a")]);
        Assert.Equal(new[] { Hash("a"), Hash("b") }, htt.Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public void Set_ExistingKey_OverwritesWithoutReordering()
    {
        var htt = new HashToTimes();
        htt.Add(Hash("a"));
        htt.Add(Hash("b"));

        htt[Hash("a")] = 99;

        Assert.Equal(99, htt[Hash("a")]);
        Assert.Equal(new[] { Hash("a"), Hash("b") }, htt.Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public void Add_BeyondMax_EvictsOldestFirst()
    {
        var htt = new HashToTimes(maxEntries: 3);

        for (var i = 0; i < 5; i++)
        {
            htt.Add(Hash($"item-{i}"));
        }

        Assert.Equal(3, htt.Count);
        Assert.False(htt.ContainsKey(Hash("item-0")));
        Assert.False(htt.ContainsKey(Hash("item-1")));
        Assert.True(htt.ContainsKey(Hash("item-2")));
        Assert.True(htt.ContainsKey(Hash("item-4")));
    }

    [Fact]
    public void Add_ExistingKey_DoesNotEvictEvenAtCapacity()
    {
        var htt = new HashToTimes(maxEntries: 2);
        htt.Add(Hash("a"));
        htt.Add(Hash("b"));

        htt.Add(Hash("a"));

        Assert.Equal(2, htt.Count);
        Assert.True(htt.ContainsKey(Hash("a")));
        Assert.True(htt.ContainsKey(Hash("b")));
    }

    [Fact]
    public void MaxEntries_Setter_TrimsOldestImmediately()
    {
        var htt = new HashToTimes(maxEntries: 5);
        for (var i = 0; i < 5; i++)
        {
            htt.Add(Hash($"item-{i}"));
        }

        htt.MaxEntries = 2;

        Assert.Equal(2, htt.Count);
        Assert.Equal(new[] { Hash("item-3"), Hash("item-4") }, htt.Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public void MaxEntries_IsClampedToAtLeastOne()
    {
        var htt = new HashToTimes(maxEntries: 0);

        htt.Add(Hash("a"));
        htt.Add(Hash("b"));

        Assert.Equal(1, htt.MaxEntries);
        Assert.Equal(1, htt.Count);
        Assert.True(htt.ContainsKey(Hash("b")));
    }

    [Fact]
    public void Snapshot_IsIndependentOfSource()
    {
        var htt = new HashToTimes();
        htt.Add(Hash("a"));

        var snapshot = htt.Snapshot();
        htt.Add(Hash("b"));

        Assert.Equal(1, snapshot.Count);
        Assert.Equal(2, htt.Count);
    }
}
