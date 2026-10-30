using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace DonOriginal3DMusic3;

// Preserve HTDemucs' four independent stems. Do not collapse drums and
// "other" back to one mono object before spatial rendering.
internal static class SpatialObjectRouter
{
    private const int Rate=48000;
    private const string Afir =
        "afir=irnorm=-1:irfmt=mono:irgain=1:dry=1:wet=1:precision=double";
    private static string Num(double x)=>
        x.ToString("0.#########",CultureInfo.InvariantCulture);

    // Audio inputs 0..4: vocals, drums, bass, other, residual.
    // Impulse inputs 5..12: front L/R, rear L/R, side-left L/R, side-right L/R.
    public static string Graph(double vocalDb,double backingDb,bool rearVocal) =>
        Graph(vocalDb,backingDb,rearVocal?1:0);

    public static string Graph(double vocalDb,double backingDb,int vocalSeat)
    {
        double voice=Math.Pow(10,vocalDb/20);
        double instr=Math.Pow(10,backingDb/20);
        if(vocalSeat<0||vocalSeat>4)
            throw new ArgumentOutOfRangeException(nameof(vocalSeat));
        string[] vl={"irFVL","irRVL","irUFVL","irUBVL","irDFVL"};
        string[] vr={"irFVR","irRVR","irUFVR","irUBVR","irDFVR"};
        string vL=vl[vocalSeat],vR=vr[vocalSeat];
        string unused=string.Join(";",vl.Where((_,i)=>i!=vocalSeat)
            .Select(x=>"["+x+"]anullsink").Concat(
             vr.Where((_,i)=>i!=vocalSeat).Select(x=>"["+x+"]anullsink")));
        string Conv(string s,string ir,string output)=>
            "["+s+"]["+ir+"]"+Afir+"["+output+"]";
        var parts=new List<string>
        {
            "[0:a]pan=mono|c0=0.5*c0+0.5*c1,volume="+Num(voice)+
                ",asplit=2[vL][vR]",
            "[1:a]volume="+Num(instr)+",asplit=3[drums0L][drums0R][drumD0]",
            "[drumD0]pan=mono|c0=0.5*c0+0.5*c1,volume=0.12,asplit=2[dDFL][dDFR]",
            "[drums0L]pan=mono|c0=c0,asplit=2[dLL][dLR]",
            "[drums0R]pan=mono|c0=c1,asplit=2[dRL][dRR]",
            "[2:a]pan=mono|c0=0.5*c0+0.5*c1,volume="+Num(instr)+
                ",asplit=2[bL][bR]",
            "[3:a]volume="+Num(instr)+
                ",asplit=3[otherL0][otherR0][otherF0]",
            "[otherL0]pan=mono|c0=c0,asplit=2[oLs][oLu]",
            "[oLs]volume=0.70,asplit=2[oLL][oLR]",
            "[oLu]volume=0.24,asplit=2[oUFL][oUFR]",
            "[otherR0]pan=mono|c0=c1,asplit=2[oRs][oRu]",
            "[oRs]volume=0.70,asplit=2[oRL][oRR]",
            "[oRu]volume=0.24,asplit=2[oUBL][oUBR]",
            "[otherF0]pan=mono|c0=0.5*c0+0.5*c1,volume=0.10,asplit=2[oFL][oFR]",
            "[4:a]pan=mono|c0=0.5*c0+0.5*c1,volume="+Num(instr)+
                ",asplit=2[resL][resR]",
            "[5:a]asplit=3[irFVL][irFOL][irFRL]",
            "[6:a]asplit=3[irFVR][irFOR][irFRR]",
            "[7:a]asplit=2[irRVL][irRBL]",
            "[8:a]asplit=2[irRVR][irRBR]",
            "[9:a]asplit=2[irSLLD][irSLLO]",
            "[10:a]asplit=2[irSLRD][irSLRO]",
            "[11:a]asplit=2[irSRLD][irSRLO]",
            "[12:a]asplit=2[irSRRD][irSRRO]",
            "[13:a]asplit=2[irUFVL][irUFOL]",
            "[14:a]asplit=2[irUFVR][irUFOR]",
            "[15:a]asplit=2[irUBVL][irUBOL]",
            "[16:a]asplit=2[irUBVR][irUBOR]",
            "[17:a]asplit=2[irDFVL][irDFDL]",
            "[18:a]asplit=2[irDFVR][irDFDR]",
            Conv("vL",vL,"vocOutL"),
            Conv("vR",vR,"vocOutR"),
            unused,
            Conv("bL","irRBL","bassOutL"),
            Conv("bR","irRBR","bassOutR"),
            Conv("dLL","irSLLD","drumLeftEarL"),
            Conv("dLR","irSLRD","drumLeftEarR"),
            Conv("dRL","irSRLD","drumRightEarL"),
            Conv("dRR","irSRRD","drumRightEarR"),
            Conv("oLL","irSLLO","otherLeftEarL"),
            Conv("oLR","irSLRO","otherLeftEarR"),
            Conv("oRL","irSRLO","otherRightEarL"),
            Conv("oRR","irSRRO","otherRightEarR"),
            Conv("oFL","irFOL","otherFrontL"),
            Conv("oFR","irFOR","otherFrontR"),
            Conv("oUFL","irUFOL","otherUpFrontL"),
            Conv("oUFR","irUFOR","otherUpFrontR"),
            Conv("oUBL","irUBOL","otherUpBackL"),
            Conv("oUBR","irUBOR","otherUpBackR"),
            Conv("dDFL","irDFDL","drumDownFrontL"),
            Conv("dDFR","irDFDR","drumDownFrontR"),
            Conv("resL","irFRL","residualFrontL"),
            Conv("resR","irFRR","residualFrontR"),
            "[vocOutL][bassOutL][drumLeftEarL][drumRightEarL]"+
                "[otherLeftEarL][otherRightEarL][otherFrontL][otherUpFrontL]"+
                "[otherUpBackL][drumDownFrontL][residualFrontL]"+
                "amix=inputs=11:normalize=0:duration=longest[L]",
            "[vocOutR][bassOutR][drumLeftEarR][drumRightEarR]"+
                "[otherLeftEarR][otherRightEarR][otherFrontR][otherUpFrontR]"+
                "[otherUpBackR][drumDownFrontR][residualFrontR]"+
                "amix=inputs=11:normalize=0:duration=longest[R]",
            "[L][R]amerge=inputs=2[out]"
        };
        string graph=string.Join(";",parts);
        ValidateGraph(graph);
        return graph;
    }

    private static void ValidateGraph(string graph)
    {
        var padCounts=Regex.Matches(graph,@"\[([A-Za-z][A-Za-z0-9_]*)\]")
            .Select(m=>m.Groups[1].Value)
            .GroupBy(x=>x,StringComparer.Ordinal)
            .ToDictionary(x=>x.Key,x=>x.Count(),StringComparer.Ordinal);
        foreach(var p in padCounts)
        {
            int wanted=p.Key=="out"?1:2;
            if(p.Value!=wanted)
                throw new InvalidDataException(
                    "Binaural graph disconnected or duplicated: "+p.Key+
                    " count="+p.Value+" expected="+wanted);
        }
        if(!padCounts.ContainsKey("out"))
            throw new InvalidDataException("Missing binaural output");
    }

    public static void MakeResidual(
        string sourcePath,string vocalPath,string drumPath,string bassPath,
        string otherPath,string destination,CancellationToken cancel)
    {
        using var source=File.OpenRead(sourcePath);
        using var voice=File.OpenRead(vocalPath);
        using var drums=File.OpenRead(drumPath);
        using var bass=File.OpenRead(bassPath);
        using var other=File.OpenRead(otherPath);
        using var output=File.Create(destination);
        Stream[] stems={voice,drums,bass,other};
        long maxDrift=Rate*2L*sizeof(float)/10;
        if(source.Length<Rate*2L*sizeof(float)||source.Length%8!=0)
            throw new InvalidDataException("Source PCM is not stereo float32");
        foreach(var x in stems)
            if(x.Length<=0||x.Length%8!=0||
               Math.Abs(x.Length-source.Length)>maxDrift)
                throw new InvalidDataException("Stem time alignment exceeds 0.1 seconds");
        const int Block=32768;
        byte[] srcBytes=new byte[Block],outBytes=new byte[Block];
        byte[][] stemBytes=stems.Select(_=>new byte[Block]).ToArray();
        while(true)
        {
            cancel.ThrowIfCancellationRequested();
            int n=source.Read(srcBytes,0,Block);
            if(n==0)break;
            while(n<Block)
            {
                int next=source.Read(srcBytes,n,Block-n);
                if(next==0)break;
                n+=next;
            }
            if(n%8!=0)throw new InvalidDataException("Invalid source frame boundary");
            for(int j=0;j<stems.Length;j++)
            {
                Array.Clear(stemBytes[j]);
                int got=0;
                while(got<n)
                {
                    cancel.ThrowIfCancellationRequested();
                    int r=stems[j].Read(stemBytes[j],got,n-got);
                    if(r==0)break;
                    got+=r;
                }
                if(got<n && stems[j].Position<stems[j].Length)
                    throw new InvalidDataException("Truncated stem data");
                if(got<n && n-got>maxDrift)
                    throw new InvalidDataException("Stem end drift too large");
            }
            var src=MemoryMarshal.Cast<byte,float>(srcBytes.AsSpan(0,n));
            var v=MemoryMarshal.Cast<byte,float>(stemBytes[0].AsSpan(0,n));
            var d=MemoryMarshal.Cast<byte,float>(stemBytes[1].AsSpan(0,n));
            var b=MemoryMarshal.Cast<byte,float>(stemBytes[2].AsSpan(0,n));
            var o=MemoryMarshal.Cast<byte,float>(stemBytes[3].AsSpan(0,n));
            var rem=MemoryMarshal.Cast<byte,float>(outBytes.AsSpan(0,n));
            for(int k=0;k<src.Length;k++)
            {
                if(!float.IsFinite(src[k])||!float.IsFinite(v[k])||
                   !float.IsFinite(d[k])||!float.IsFinite(b[k])||
                   !float.IsFinite(o[k]))
                    throw new InvalidDataException("Invalid source/stem PCM values");
                rem[k]=src[k]-(v[k]+d[k]+b[k]+o[k]);
            }
            output.Write(outBytes,0,n);
        }
    }

    public static void SelfTest()
    {
        string front=Graph(1,-0.5,false);
        string rear=Graph(1,-0.5,true);
        string upFront=Graph(1,-0.5,2);
        string upBack=Graph(1,-0.5,3);
        string downFront=Graph(1,-0.5,4);
        static void Require(bool pass,string reason)
        {
            if(!pass)throw new InvalidOperationException("4-stem 3D self-test: "+reason);
        }
        Require(front.Contains("[vL][irFVL]")&&
                front.Contains("[vR][irFVR]")&&
                rear.Contains("[vL][irRVL]")&&
                rear.Contains("[vR][irRVR]"),"vocal front/rear");
        Require(upFront.Contains("[vL][irUFVL]")&&
                upBack.Contains("[vL][irUBVL]")&&
                downFront.Contains("[vL][irDFVL]"),
                "true measured elevated front/up-back/down-front H3 IR positioning");
        Require(front.Contains("[oUFL][irUFOL]")&&
                front.Contains("[oUBL][irUBOL]")&&
                front.Contains("[dDFL][irDFDL]"),
                "measured elevated musical ambience paths");
        Require(front.Contains("[dLL][irSLLD]")&&
                front.Contains("[dRL][irSRLD]")&&
                front.Contains("[oLL][irSLLO]")&&
                front.Contains("[oRL][irSRLO]")&&
                front.Contains("[bL][irRBL]"),"four independent directions");
        bool dangling=false;
        try{ValidateGraph(front.Replace(
            "[5:a]asplit=3[irFVL][irFOL][irFRL]",
            "[5:a]asplit=4[unconnected][irFVL][irFOL][irFRL]"));}
        catch(InvalidDataException){dangling=true;}
        Require(dangling,"reject unused impulse connection");
        string root=Path.Combine(Path.GetTempPath(),
            "DonOriginal3DMusic3_4stem_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const int frames=Rate;
            void Write(string name,float value)
            {
                float[] samples=Enumerable.Repeat(value,frames*2).ToArray();
                byte[] bytes=new byte[samples.Length*sizeof(float)];
                Buffer.BlockCopy(samples,0,bytes,0,bytes.Length);
                File.WriteAllBytes(Path.Combine(root,name),bytes);
            }
            Write("src.f32",0.25f); Write("voc.f32",0.12f);
            Write("drums.f32",0.03f); Write("bass.f32",0.08f);
            Write("other.f32",0.01f);
            MakeResidual(Path.Combine(root,"src.f32"),
                Path.Combine(root,"voc.f32"),
                Path.Combine(root,"drums.f32"),
                Path.Combine(root,"bass.f32"),
                Path.Combine(root,"other.f32"),
                Path.Combine(root,"residual.f32"),CancellationToken.None);
            byte[] bytes=File.ReadAllBytes(Path.Combine(root,"residual.f32"));
            Require(bytes.Length==frames*2*sizeof(float),"residual duration");
            float[] values=new float[frames*2];
            Buffer.BlockCopy(bytes,0,values,0,bytes.Length);
            Require(Math.Abs(values[345]-0.01f)<0.00001,"additive model residual");
        }
        finally {try{Directory.Delete(root,true);}catch{}}
    }
}
