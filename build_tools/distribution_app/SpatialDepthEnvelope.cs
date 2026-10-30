using System;
using System.IO;
using System.Linq;

namespace DonOriginal3DMusic3;

/// <summary>
/// Per-object binaural IR envelope. Three conceptual ranges use DIFFERENT
/// direct/early/late ratios but do not claim to be measured near-field HRTFs.
/// The same smooth envelope is applied to L and R to preserve interaural cues.
/// </summary>
internal static class SpatialDepthEnvelope
{
    internal const int SampleRate=48000;
    internal enum Depth { Foreground, Midfield, Background }

    internal static void Apply(float[] left,float[] right,Depth depth,
        double roomPercent)
    {
        if(left.Length!=right.Length||left.Length<SampleRate/5)
            throw new InvalidDataException("左右の測定IR長が一致しません");
        if(!double.IsFinite(roomPercent)||roomPercent<20||roomPercent>100)
            throw new ArgumentOutOfRangeException(nameof(roomPercent));
        // New 3D mode: 45% is NOT a direct 0.45 scaling of the entire
        // reflected field. That old implementation erased envelopment.
        double wet=0.70+0.30*roomPercent/100.0;
        double direct,early,late;
        switch(depth)
        {
            case Depth.Foreground:
                direct=1.0;early=0.91;late=0.77*wet;
                break;
            case Depth.Midfield:
                direct=0.92;early=1.00;late=0.96*wet;
                break;
            default:
                direct=0.76;early=0.98;late=1.12*wet;
                break;
        }
        int onset=FindCommonOnset(left,right);
        int directEnd=onset+(int)(.008*SampleRate);
        int earlyStart=onset+(int)(.014*SampleRate);
        int lateStart=onset+(int)(.068*SampleRate);
        int lateEnd=onset+(int)(.090*SampleRate);
        for(int i=0;i<left.Length;i++)
        {
            double first=Smooth(i,directEnd,earlyStart);
            double second=Smooth(i,lateStart,lateEnd);
            double gain=direct+(early-direct)*first+(late-early)*second;
            left[i]=(float)(left[i]*gain);
            right[i]=(float)(right[i]*gain);
        }
    }

    internal static int FindCommonOnset(float[] left,float[] right)
    {
        int n=Math.Min(4800,left.Length);
        int best=0;double energy=0;
        for(int i=0;i<n;i++)
        {
            double l=left[i],r=right[i];
            if(!double.IsFinite(l)||!double.IsFinite(r))
                throw new InvalidDataException("IRに非有限値があります");
            double value=l*l+r*r;
            if(value>energy){energy=value;best=i;}
        }
        if(energy<1e-18)throw new InvalidDataException("IR直接音を検出できません");
        return best;
    }

    private static double Smooth(int i,int start,int end)
    {
        double t=Math.Clamp((i-start)/(double)(end-start),0,1);
        return t*t*(3-2*t);
    }

    internal static (double earlyToLateDb,double crossEarCorrelation)
        Inspect(float[] left,float[] right)
    {
        int onset=FindCommonOnset(left,right);
        int cut=Math.Min(left.Length,onset+(int)(.080*SampleRate));
        double eEarly=1e-30,eLate=1e-30,l2=1e-30,r2=1e-30,lr=0;
        for(int i=0;i<left.Length;i++)
        {
            double l=left[i],r=right[i];
            double sum=l*l+r*r;
            if(i<=cut){eEarly+=sum;l2+=l*l;r2+=r*r;lr+=l*r;}
            else eLate+=sum;
        }
        return (10*Math.Log10(eEarly/eLate),lr/Math.Sqrt(l2*r2));
    }

    internal static void SelfTest()
    {
        static void Check(bool pass,string description)
        {
            if(!pass)throw new InvalidOperationException(
                "SpatialDepthEnvelope self-test: "+description);
        }
        float[] Create()
        {
            var a=new float[SampleRate];
            a[600]=.60f; // direct
            a[1200]=.15f; // early
            a[12000]=.04f; // late
            return a;
        }
        var nearL=Create();var nearR=Create();
        var farL=Create();var farR=Create();
        Apply(nearL,nearR,Depth.Foreground,45);
        Apply(farL,farR,Depth.Background,45);
        Check(Math.Abs(nearL[600]-.60f)<1e-7f &&
              Math.Abs(nearR[600]-.60f)<1e-7f,
              "foreground direct sound must remain unchanged");
        Check(Math.Abs(farL[600]-.456f)<1e-6f &&
              Math.Abs(farR[600]-.456f)<1e-6f,
              "far-field direct level controlled, not added");
        var near=Inspect(nearL,nearR).earlyToLateDb;
        var far=Inspect(farL,farR).earlyToLateDb;
        Check(near>far+2.0,
              "relative depth must create ordered direct/reverberant ratios");
        Check(nearL.SequenceEqual(nearR)&&farL.SequenceEqual(farR),
              "equal L/R envelope must preserve unchanged interaural balance");
    }
}
