using CgfConverter.Models.Structs;
using CgfConverter.CryEngineCore;
using CgfConverter.CryEngineCore.Chunks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace CgfConverterTests.UnitTests;

[TestClass]
public class IvoAnimationDecodingTests
{
    [TestMethod]
    public void PackedPositionKeysReadSeparateAxisArrays()
    {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        WriteHeader(w, new(1, float.MaxValue, 2), new(10, 20, 30));
        foreach(ushort value in new ushort[]{1,2,3,100,200,300}) w.Write(value);
        stream.Position=0; using var reader=new BinaryReader(stream);
        var p=IvoAnimationHelpers.ReadPositionKeys(reader,3,0xC242);
        Assert.AreEqual(new Vector3(11,20,230),p[0]);
        Assert.AreEqual(new Vector3(12,20,430),p[1]);
        Assert.AreEqual(new Vector3(13,20,630),p[2]);
        Assert.AreEqual(stream.Length,stream.Position);
    }

    [TestMethod]
    public void PackedPositionsWithOneActiveAxisKeepConstantAxes()
    {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        WriteHeader(w,new(float.MaxValue,0.5f,float.MaxValue),new(1,2,3));
        w.Write((ushort)4);w.Write((ushort)6);stream.Position=0;
        using var r=new BinaryReader(stream);var p=IvoAnimationHelpers.ReadPositionKeys(r,2,0xC240);
        Assert.AreEqual(new Vector3(1,4,3),p[0]);Assert.AreEqual(new Vector3(1,5,3),p[1]);
    }

    [TestMethod]
    public void FullPositionFormatRemainsInterleaved()
    {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        WriteHeader(w,Vector3.One,Vector3.Zero);
        foreach(ushort v in new ushort[]{1,2,3,4,5,6})w.Write(v);stream.Position=0;
        using var r=new BinaryReader(stream);var p=IvoAnimationHelpers.ReadPositionKeys(r,2,0xC142);
        Assert.AreEqual(new Vector3(1,2,3),p[0]);Assert.AreEqual(new Vector3(4,5,6),p[1]);
    }

    [TestMethod]
    public void BitmapTimesPreserveSparseFramesAndNonzeroStart()
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
        w.Write((ushort)10);w.Write((ushort)26);w.Write(new byte[]{0x85,0x80,0x01});stream.Position=0;
        using var r=new BinaryReader(stream);var times=IvoAnimationHelpers.ReadTimeKeys(r,5,0xC242);
        CollectionAssert.AreEqual(new float[]{10,12,17,25,26},times.ToArray());
        Assert.AreEqual(stream.Length,stream.Position);
    }

    [TestMethod]
    public void BitmapCountMismatchIsReportedInsteadOfInventingTimes()
    {
        using var r=new BinaryReader(new MemoryStream(new byte[]{0,0,7,0,3}));
        Assert.ThrowsException<InvalidDataException>(()=>IvoAnimationHelpers.ReadTimeKeys(r,3,0x8242));
    }

    [TestMethod]
    public void TruncatedBitmapIsReported()
    {
        using var r=new BinaryReader(new MemoryStream(new byte[]{0,0,16,0,1}));
        Assert.ThrowsException<EndOfStreamException>(()=>IvoAnimationHelpers.ReadTimeKeys(r,1,0xC242));
    }

    [TestMethod]
    public void ExplicitByteAndUshortTimesRetainExactFrames()
    {
        using var rb=new BinaryReader(new MemoryStream(new byte[]{0,5,65}));
        CollectionAssert.AreEqual(new float[]{0,5,65},IvoAnimationHelpers.ReadTimeKeys(rb,3,0xC240).ToArray());
        using var rs=new BinaryReader(new MemoryStream(new byte[]{0,0,44,1,232,3}));
        CollectionAssert.AreEqual(new float[]{0,300,1000},IvoAnimationHelpers.ReadTimeKeys(rs,3,0xC241).ToArray());
    }

    [TestMethod]
    public void RealZeusDoorHasCorrectAxisPairingAndSparseTimes()
    {
        string path=Environment.GetEnvironmentVariable("SC_ZEUS_DBA");
        if(string.IsNullOrEmpty(path))Assert.Inconclusive("Set SC_ZEUS_DBA to the original database.");
        using var stream=File.OpenRead(path);var model=Model.FromStream("Zeus.dba",stream);
        var data=model.ChunkMap.Values.OfType<ChunkIvoDBAData>().Single();
        Assert.AreEqual(79,data.AnimationBlocks.Count);
        var open=data.AnimationBlocks[17];var p=open.Positions[0x048DB017];
        Assert.AreEqual(44,p.Count);
        Assert.AreEqual(-0.001919315f,p[0].Z,0.000001f);
        Assert.AreEqual(-0.581211f,p[^1].Z,0.000001f);
        CollectionAssert.AreEqual(new float[]{0,1,2,3,4,7},open.PositionTimes[0x048DB017].Take(6).ToArray());
        Assert.AreEqual(21f,open.PositionTimes[0x1F525113][1]);
        Assert.AreEqual(0.761744f,open.Positions[0x1F525113][0].Z,0.000001f);
        string output=Environment.GetEnvironmentVariable("SC_ZEUS_DECODE_OUTPUT");
        if(!string.IsNullOrEmpty(output))
        {
            var snapshot=data.AnimationBlocks.Select(block=>new {
                tracks=block.Positions.ToDictionary(k=>k.Key.ToString("X8"),v=>new {
                    times=block.PositionTimes[v.Key],
                    positions=v.Value.Select(p=>new[]{p.X,p.Y,p.Z}).ToArray()
                })
            }).ToArray();
            File.WriteAllText(output,JsonSerializer.Serialize(snapshot));
        }
    }

    private static void WriteHeader(BinaryWriter w,Vector3 s,Vector3 o)
    {
        w.Write(s.X);w.Write(s.Y);w.Write(s.Z);w.Write(o.X);w.Write(o.Y);w.Write(o.Z);
    }
}
