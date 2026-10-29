using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace DonOriginal3DMusic3;

internal static class FfmpegService
{
    public static async Task RunAsync(string exe, IEnumerable<string> args, CancellationToken token)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo {
            FileName = exe, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardError = true, RedirectStandardOutput = true
        };
        foreach (string arg in args) p.StartInfo.ArgumentList.Add(arg);
        if (!p.Start()) throw new InvalidOperationException("FFmpegを起動できません。");
        using var reg = token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } });
        // Read both pipes concurrently to prevent a full output pipe hanging FFmpeg.
        var errTask = p.StandardError.ReadToEndAsync(token);
        var outTask = p.StandardOutput.ReadToEndAsync(token);
        await Task.WhenAll(errTask, outTask, p.WaitForExitAsync(token));
        if (p.ExitCode != 0) throw new InvalidOperationException(
            "FFmpegエラー (" + p.ExitCode + "): " + errTask.Result.Trim());
    }
}

internal static class DistributionAudioEngine
{
    private const int Rate = 48000;
    private const string Afir =
        "afir=irnorm=-1:irfmt=mono:irgain=1:dry=1:wet=1:precision=double";
    private static string F(double d) => d.ToString("0.#########", CultureInfo.InvariantCulture);
    private static string Ff => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin", "ffmpeg.exe");
    private static string Fp => Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg", "bin", "ffprobe.exe");
    private static string Sofa => Path.Combine(AppContext.BaseDirectory, "assets", "hrtf", "hutubs_pp3.sofa");
    private static string Ir(string direction, string mode, string ear) =>
        Path.Combine(AppContext.BaseDirectory, "assets", "brir", "iosr_" +
            direction + "_" + mode + "_" + ear + ".wav");

    public static IReadOnlyList<string> MissingAssets()
    {
        var req = new List<string> { Ff, Fp, Sofa };
        foreach (string dir in new[] {"F", "B"})
            foreach (string ear in new[] {"L", "R"})
                req.Add(Ir(dir, "room", ear));
        foreach (string side in new[] {"sideL_room", "sideR_room"})
            foreach (string ear in new[] {"L", "R"})
                req.Add(Ir("F", side, ear));
        return req.Where(x => !File.Exists(x)).Concat(
            StemSeparatorService.AssetsAvailable(out string m) ? Array.Empty<string>() :
            new [] {"音源分離エンジン/モデル: " + m}).ToArray();
    }

    private static async Task DecodeAsync(string input, string output, int channels,
        CancellationToken token)
    {
        await FfmpegService.RunAsync(Ff, new[] {
            "-hide_banner", "-loglevel", "error", "-y", "-i", input,
            "-map", "0:a:0", "-vn", "-ar", "48000", "-ac", channels.ToString(),
            "-f", "f32le", output
        }, token);
        if (!File.Exists(output) || new FileInfo(output).Length < 4096)
            throw new InvalidDataException("音声のPCM展開に失敗: " + input);
    }

    private static float[] ReadFloat(string file)
    {
        byte[] bytes = File.ReadAllBytes(file); // short impulse response, not a full song.
        if (bytes.Length == 0 || bytes.Length % 4 != 0)
            throw new InvalidDataException("IR PCMのサイズが不正です。");
        float[] values = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
        if (values.Any(x => !float.IsFinite(x)))
            throw new InvalidDataException("IRに非有限値が含まれます。");
        return values;
    }

    private static void WriteWavMono(string path, float[] samples)
    {
        using var w = new BinaryWriter(File.Create(path));
        int dataBytes = checked(samples.Length * 4);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + dataBytes);
        w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)3); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 4); w.Write((short)4); w.Write((short)32);
        w.Write(Encoding.ASCII.GetBytes("data"));
        w.Write(dataBytes);
        foreach (float value in samples) w.Write(value);
    }

    private static int FirstDirectPeak(float[] audio, int maxLook = 4800)
    {
        int max = Math.Min(maxLook, audio.Length);
        if (max == 0) throw new InvalidDataException("IRが空です");
        int idx = 0;
        float best = 0;
        for (int i=0; i<max; i++) {
            float amplitude = Math.Abs(audio[i]);
            if (amplitude > best) { best = amplitude; idx = i; }
        }
        if (best < 1e-9) throw new InvalidDataException("実測IR直接音を検出できません");
        return idx;
    }

    private static int PairPeak(float[] stereo)
    {
        int n = Math.Min(stereo.Length / 2, 4800);
        int bestIndex = 0; double best = -1;
        for (int i=0; i<n; i++) {
            double e = (double)stereo[i*2]*stereo[i*2] +
                       (double)stereo[i*2+1]*stereo[i*2+1];
            if (e > best) { best = e; bestIndex = i; }
        }
        if (best <= 1e-18) throw new InvalidDataException("H3測定IRの直接音を検出できません");
        return bestIndex;
    }

    private static (float[] L, float[] R) ReplaceDirect(
        float[] originalL, float[] originalR, float[] hrtfStereo)
    {
        if (hrtfStereo.Length % 2 != 0) throw new InvalidDataException("HRIRの左右長不正");
        if (originalL.Length < 2000 || originalR.Length < 2000)
            throw new InvalidDataException("実測Room IRが短すぎます");
        // Use ONE common alignment/gain for both ears; preserve H3 ILD/ITD.
        int roomPeak = Math.Min(FirstDirectPeak(originalL), FirstDirectPeak(originalR));
        int hrtfPeak = PairPeak(hrtfStereo);
        int onset = Math.Max(0, roomPeak - 144); // 3 ms early window
        int fadeStart = onset + 144; // 3 ms
        int fadeEnd = onset + 480;   // 10 ms, KEEP all later room samples
        float[] l = (float[])originalL.Clone(), r = (float[])originalR.Clone();
        double eroom = 0, ehrtf = 0;
        for (int i=onset; i<fadeEnd && i<l.Length && i<r.Length; i++) {
            eroom += (double)originalL[i]*originalL[i] +
                     (double)originalR[i]*originalR[i];
            int hIdx = i - roomPeak + hrtfPeak;
            if (hIdx >= 0 && hIdx*2+1 < hrtfStereo.Length)
                ehrtf += (double)hrtfStereo[hIdx*2]*hrtfStereo[hIdx*2] +
                         (double)hrtfStereo[hIdx*2+1]*hrtfStereo[hIdx*2+1];
        }
        if (ehrtf < 1e-16 || eroom < 1e-16)
            throw new InvalidDataException("H3/Room直接音の共通エネルギー合わせができません");
        double gain = Math.Sqrt(eroom/ehrtf);
        if (gain > 1e3 || gain < 1e-4)
            throw new InvalidDataException("H3とRoom IRの相対レベルが極端です");
        for (int i=onset; i<fadeEnd && i<l.Length && i<r.Length; i++) {
            int hIdx = i - roomPeak + hrtfPeak;
            double hL = 0, hR = 0;
            if (hIdx >= 0 && hIdx*2+1 < hrtfStereo.Length) {
                hL = hrtfStereo[hIdx*2]*gain;
                hR = hrtfStereo[hIdx*2+1]*gain;
            }
            double blend = i <= fadeStart ? 1 :
                Math.Clamp((double)(fadeEnd-i)/(fadeEnd-fadeStart),0,1);
            blend = blend*blend*(3-2*blend); // gradual return to REAL room IR
            l[i] = (float)(hL*blend + originalL[i]*(1-blend));
            r[i] = (float)(hR*blend + originalR[i]*(1-blend));
        }
        // Samples i >= fadeEnd have NOT been modified.
        return (l, r);
    }

    private static void ScaleLateRoomReflections(float[] left,float[] right,double percent)
    {
        if(!double.IsFinite(percent) || percent<20 || percent>100)
            throw new ArgumentOutOfRangeException(nameof(percent));
        if(left.Length!=right.Length || left.Length<Rate/4)
            throw new InvalidDataException("実測室内IRの左右長が不正です");
        if(percent>=100)return;
        // Keep H3 direct and first 18 ms of measured early room.
        // Attenuate only later reflections with equal gains on both ears.
        int onset=Math.Min(FirstDirectPeak(left),FirstDirectPeak(right));
        int start=onset+(int)(Rate*0.018);
        int end=onset+(int)(Rate*0.105);
        double target=percent/100d;
        for(int i=start;i<left.Length;i++) {
            double t=Math.Clamp((i-start)/(double)(end-start),0,1);
            double smooth=t*t*(3-2*t);
            float g=(float)(1-(1-target)*smooth);
            left[i]*=g;right[i]*=g;
        }
    }

    private static async Task<(string L,string R)> BuildSideRoomIr(
        string temp,string side,double percent,CancellationToken token)
    {
        string lpcm=Path.Combine(temp,"side_"+side+"_L.f32");
        string rpcm=Path.Combine(temp,"side_"+side+"_R.f32");
        await DecodeAsync(Ir("F",side+"_room","L"),lpcm,1,token);
        await DecodeAsync(Ir("F",side+"_room","R"),rpcm,1,token);
        float[] left=ReadFloat(lpcm),right=ReadFloat(rpcm);
        ScaleLateRoomReflections(left,right,percent);
        string lfile=Path.Combine(temp,"side_"+side+"_L.wav");
        string rfile=Path.Combine(temp,"side_"+side+"_R.wav");
        WriteWavMono(lfile,left);WriteWavMono(rfile,right);
        return (lfile,rfile);
    }

    private static async Task<(string L,string R)> BuildHybridIr(
        string temp, string direction, int angle, double roomPercent,
        CancellationToken token)
    {
        string mono = Path.Combine(temp, "test_impulse.f32");
        if (!File.Exists(mono)) {
            float[] pulse = new float[Rate];
            pulse[0] = 0.5f;
            byte[] data = new byte[pulse.Length * 4];
            Buffer.BlockCopy(pulse, 0, data, 0, data.Length);
            await File.WriteAllBytesAsync(mono, data, token);
        }
        string hrtfF32 = Path.Combine(temp, "H3_" + direction + ".f32");
        await FfmpegService.RunAsync(Ff, new[] {
            "-hide_banner", "-loglevel", "error", "-y",
            "-f", "f32le", "-ar", "48000", "-ac", "1", "-i", mono,
            "-af", "pan=7.1|FC=c0,sofalizer=sofa='assets/hrtf/hutubs_pp3.sofa':" +
                   "type=freq:speakers='FC " + angle + " 0':" +
                   "interpolate=1:anglestep=1:normalize=0:lfegain=-20",
            "-ar", "48000", "-ac", "2", "-f", "f32le", hrtfF32
        }, token);
        string originalL = Path.Combine(temp, direction + "_original_L.f32");
        string originalR = Path.Combine(temp, direction + "_original_R.f32");
        await DecodeAsync(Ir(direction, "room", "L"), originalL, 1, token);
        await DecodeAsync(Ir(direction, "room", "R"), originalR, 1, token);
        var both = ReplaceDirect(ReadFloat(originalL), ReadFloat(originalR), ReadFloat(hrtfF32));
        ScaleLateRoomReflections(both.L,both.R,roomPercent);
        string outL = Path.Combine(temp, "v1.0_" + direction + "_Hybrid_L.wav");
        string outR = Path.Combine(temp, "v1.0_" + direction + "_Hybrid_R.wav");
        WriteWavMono(outL, both.L);
        WriteWavMono(outR, both.R);
        return (outL,outR);
    }

    private static void ReconstructBuses(string source,string vocals,string bass,
        string vocalOut,string bassOut,string backOut,CancellationToken token)
    {
        long originalBytes = new FileInfo(source).Length;
        long vocalBytes = new FileInfo(vocals).Length;
        long bassBytes = new FileInfo(bass).Length;
        if(originalBytes % 8 != 0 || vocalBytes % 8 != 0 || bassBytes % 8 != 0)
            throw new InvalidDataException("原音/分離StemのPCMがステレオfloat32ではありません");
        if(originalBytes <= 0 || vocalBytes <= 0 || bassBytes <= 0)
            throw new InvalidDataException("原音/分離StemのPCMが空です");
        // Per-track duration drift beyond 0.10s is NOT safe to pad with silence:
        // that would erase vocals or bass across an entire musical passage.
        const long allowedEndDrift = Rate * 2L * sizeof(float) / 10;
        if(Math.Abs(originalBytes-vocalBytes)>allowedEndDrift ||
           Math.Abs(originalBytes-bassBytes)>allowedEndDrift)
            throw new InvalidDataException(
                "HTDemucs分離Stemと原曲の長さが0.1秒以上異なります。音声欠落を防ぐため変換を停止します。");
        using var a=File.OpenRead(source);
        using var b=File.OpenRead(vocals);
        using var c=File.OpenRead(bass);
        using var vo=File.Create(vocalOut);
        using var bo=File.Create(bassOut);
        using var bk=File.Create(backOut);
        const int Block=32768; // multiple of stereo 32-bit sample frame
        byte[] aa=new byte[Block],bb=new byte[Block],cc=new byte[Block],dd=new byte[Block];
        long total=0; double sum=0;
        while(true) {
            token.ThrowIfCancellationRequested();
            int n=a.Read(aa,0,Block);
            if (n==0) break;
            // FileStream.ReadAsync may read fewer bytes; gather a whole block.
            while(n < Block) {
                int next=a.Read(aa,n,Block-n);
                if(next==0)break; n+=next;
            }
            if(n%8!=0)throw new InvalidDataException("Stereo float32境界に失敗");
            Array.Clear(bb); Array.Clear(cc);
            ReadAllUpTo(b,bb,n,token);
            ReadAllUpTo(c,cc,n,token);
            var src=MemoryMarshal.Cast<byte,float>(aa.AsSpan(0,n));
            var voc=MemoryMarshal.Cast<byte,float>(bb.AsSpan(0,n));
            var bas=MemoryMarshal.Cast<byte,float>(cc.AsSpan(0,n));
            var complement=MemoryMarshal.Cast<byte,float>(dd.AsSpan(0,n));
            for(int i=0;i<src.Length;i+=2) {
                float mid=(bas[i]+bas[i+1])*0.5f;
                bas[i]=mid; bas[i+1]=mid;
                for(int ear=0;ear<2;ear++) {
                    int j=i+ear;
                    if(!float.IsFinite(src[j]) || !float.IsFinite(voc[j]) || !float.IsFinite(bas[j]))
                        throw new InvalidDataException("PCM非有限値");
                    complement[j]=src[j]-voc[j]-mid;
                    sum+=(double)voc[j]*voc[j];
                }
            }
            vo.Write(bb,0,n);
            bo.Write(cc,0,n);
            bk.Write(dd,0,n);
            total+=n;
        }
        if(total<48000*8 || sum<1e-10)
            throw new InvalidDataException("原音の長さまたはボーカル分離に問題があります");
    }

    private static void ReadAllUpTo(Stream s,byte[] buffer,int size,CancellationToken token)
    {
        int offset=0;
        while(offset<size) {
            token.ThrowIfCancellationRequested();
            int got=s.Read(buffer,offset,size-offset);
            if(got==0)break;
            offset+=got;
        }
        // Initial duration sanity is enforced in ReconstructBuses. Only the
        // last <=0.10s residual padding is now permitted here.
        if(offset < size - Rate * 2 * sizeof(float) / 10)
            throw new InvalidDataException("分離Stemと原音の尺が異なります");
    }

    // Fail before invoking FFmpeg when a generated filtergraph contains an
    // unconsumed or multiply-consumed named pad (e.g. the old irFLL bug).
    // Numeric :a inputs are external streams; [out] is the sole final output.
    private static void ValidateFilterGraphTopology(string graph)
    {
        var pads=Regex.Matches(graph,@"\\[([A-Za-z][A-Za-z0-9_]*)\\]")
            .Select(m=>m.Groups[1].Value)
            .GroupBy(name=>name,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.Count(),StringComparer.Ordinal);
        foreach(var pad in pads)
        {
            int expected=pad.Key=="out" ? 1 : 2;
            if(pad.Value!=expected)
                throw new InvalidDataException(
                    "3D音響フィルター配線エラー: "+
                    pad.Key+" の接続数="+pad.Value+" (期待="+expected+")");
        }
        if(!pads.ContainsKey("out"))
            throw new InvalidDataException("3D音響フィルター最終出力がありません");
    }

    private static string Filter(double voiceDb,double backingDb,bool vocalRear)
    {
        double vg=Math.Pow(10,voiceDb/20),ig=Math.Pow(10,backingDb/20);
        // A single vocal Stem is a single positionable object; it is NOT
        // evidence of singer identities. Route the full lead to front or rear.
        string vL=vocalRear?"irRLV":"irFLV";
        string vR=vocalRear?"irRRV":"irFRV";
        string sink=vocalRear?
            "[irFLV]anullsink;[irFRV]anullsink;" :
            "[irRLV]anullsink;[irRRV]anullsink;";
        string graph=
            "[0:a]aformat=channel_layouts=stereo,asplit=2[v0][vs0];"+
            "[v0]pan=mono|c0=0.5*c0+0.5*c1,volume="+F(vg)+",asplit=2[vFL][vFR];"+
            "[vs0]pan=mono|c0=0.5*c0-0.5*c1,volume="+F(vg)+"[vSide];"+
            "[1:a]aformat=channel_layouts=stereo,pan=mono|c0=0.5*c0+0.5*c1,asplit=2[bF0][bR0];"+
            // Legacy 72%-front routing undermined the requested rear bass.
            // Mostly rear with a small front anchor (not fake bass boosting).
            "[bF0]volume=0.1,asplit=2[bFL][bFR];"+
            "[bR0]volume=0.9,asplit=2[bRL][bRR];"+
            "[2:a]aformat=channel_layouts=stereo,asplit=2[backM0][backS0];"+
            "[backM0]pan=mono|c0=0.5*c0+0.5*c1,volume="+F(ig)+",asplit=2[backRL][backRR];"+
            "[backS0]pan=mono|c0=0.5*c0-0.5*c1,volume="+F(ig)+"[backSide];"+
            "[vSide][backSide]amix=inputs=2:normalize=0:duration=longest,asplit=2[sL0][sR0];"+
            "[sL0]asplit=2[sLL][sLR];"+
            "[sR0]volume=-1,asplit=2[sRL][sRR];"+
            "[3:a]asplit=2[irFLB][irFLV];"+
            "[4:a]asplit=2[irFRB][irFRV];"+
            "[5:a]asplit=3[irRLL][irRLB][irRLV];"+
            "[6:a]asplit=3[irRRL][irRRB][irRRV];"+
            "[vFL]["+vL+"]"+Afir+"[vOL];"+
            "[vFR]["+vR+"]"+Afir+"[vOR];"+
            sink+
            "[bFL][irFLB]"+Afir+"[bfOL];"+
            "[bFR][irFRB]"+Afir+"[bfOR];"+
            "[backRL][irRLL]"+Afir+"[rOL];"+
            "[backRR][irRRL]"+Afir+"[rOR];"+
            "[bRL][irRLB]"+Afir+"[brOL];"+
            "[bRR][irRRB]"+Afir+"[brOR];"+
            "[sLL][7:a]"+Afir+"[sOLL];"+
            "[sLR][8:a]"+Afir+"[sOLR];"+
            "[sRL][9:a]"+Afir+"[sORL];"+
            "[sRR][10:a]"+Afir+"[sORR];"+
            "[vOL][bfOL][rOL][brOL][sOLL][sORL]amix=inputs=6:normalize=0:duration=longest[L];"+
            "[vOR][bfOR][rOR][brOR][sOLR][sORR]amix=inputs=6:normalize=0:duration=longest[R];"+
            "[L][R]amerge=inputs=2[out]";
        ValidateFilterGraphTopology(graph);
        return graph;
    }

    private readonly record struct Stats(long Frames,double Rms,double Peak);
    private static Stats ScanRaw(string raw,long frameLimit=long.MaxValue)
    {
        const int Block=32768;
        double ss=0,peak=0;long count=0;
        using var input=File.OpenRead(raw);
        byte[] bytes=new byte[Block];
        long sampleLimit = frameLimit == long.MaxValue ?
            long.MaxValue : checked(frameLimit * 2);
        while(count<sampleLimit) {
            int amount=(int)Math.Min(Block,Math.Min(long.MaxValue/4,sampleLimit-count)*4L);
            int n=input.Read(bytes,0,amount);
            if(n==0)break;
            if(n%4!=0)throw new InvalidDataException("PCM境界エラー");
            foreach(float x in MemoryMarshal.Cast<byte,float>(bytes.AsSpan(0,n))) {
                if(!float.IsFinite(x))throw new InvalidDataException("非有限PCM");
                ss+=(double)x*x;peak=Math.Max(peak,Math.Abs(x));count++;
            }
        }
        if(count<=0 || count%2!=0)throw new InvalidDataException("PCMが空です");
        return new Stats(count/2,Math.Sqrt(ss/count),peak);
    }

    private static string Sanitize(string title)
    {
        const string invalid = "<>:\"/\\|?*";
        var s=new StringBuilder();
        foreach(char c in title.Trim())s.Append(char.IsControl(c) || invalid.Contains(c) ? '_' : c);
        string safe=s.ToString().Trim(' ','.');
        if(string.IsNullOrWhiteSpace(safe))safe="Music";
        if(safe.Length>96)safe=safe[..96].Trim(' ','.');
        // Win32 reserved device names cannot be used as filenames.
        string stem=safe.Split('.')[0].ToUpperInvariant();
        if(new[]{"CON","PRN","AUX","NUL",
                 "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
                 "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"}.Contains(stem))
            safe="_"+safe;
        return safe;
    }

    // Read format Title first, then stream Title if present. Metadata lookup
    // failures must never interrupt conversion or change its original audio.
    private static string? ReadTagTitle(JsonElement tags)
    {
        if(tags.ValueKind != JsonValueKind.Object)return null;
        foreach(var property in tags.EnumerateObject())
            if(property.Name.Equals("title",StringComparison.OrdinalIgnoreCase) &&
               property.Value.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(property.Value.GetString()))
                return property.Value.GetString();
        return null;
    }

    private static string? ReadSourceTitleJson(JsonElement root)
    {
        if(root.ValueKind != JsonValueKind.Object)return null;
        if(root.TryGetProperty("format",out var format) &&
           format.TryGetProperty("tags",out var tags))
        {
            var value=ReadTagTitle(tags);
            if(!string.IsNullOrWhiteSpace(value))return value;
        }
        if(root.TryGetProperty("streams",out var streams) &&
           streams.ValueKind == JsonValueKind.Array)
        {
            foreach(var stream in streams.EnumerateArray())
                if(stream.ValueKind == JsonValueKind.Object &&
                   stream.TryGetProperty("tags",out var streamTags))
                {
                    var value=ReadTagTitle(streamTags);
                    if(!string.IsNullOrWhiteSpace(value))return value;
                }
        }
        return null;
    }

    // FFprobe JSON is UTF-8, independently of the Windows ANSI code page.
    // Without an explicit encoding, Japanese Windows can decode it as CP932,
    // turning さよならエンドロール into 縺輔ｈ縺ｪ繧峨お繝ｳ繝峨Ο繝ｼ繝ｫ.
    private static ProcessStartInfo CreateFfprobeStartInfo() => new()
    {
        FileName=Fp,
        WorkingDirectory=AppContext.BaseDirectory,
        RedirectStandardOutput=true,
        RedirectStandardError=true,
        UseShellExecute=false,
        CreateNoWindow=true,
        StandardOutputEncoding=new UTF8Encoding(false,true),
        StandardErrorEncoding=new UTF8Encoding(false,true)
    };

    // Restore the v1.48 rule: a valid source disc number is doubled on output.
    // FLAC tags commonly use DISCNUMBER; some formats use DISC or DISC_NUMBER.
    private static int? ReadDiscNumberTags(JsonElement tags)
    {
        if(tags.ValueKind!=JsonValueKind.Object)return null;
        foreach(string preferredKey in new[]{"discnumber","disc","disc_number",
            "disknumber","disk","part_of_set","tpos"})
            foreach(var field in tags.EnumerateObject())
                if(field.Name.Equals(preferredKey,StringComparison.OrdinalIgnoreCase) &&
                   field.Value.ValueKind==JsonValueKind.String)
                {
                    string? raw=field.Value.GetString();
                    string numberPart=(raw??"").Split('/')[0].Trim();
                    if(int.TryParse(numberPart,NumberStyles.Integer,
                        CultureInfo.InvariantCulture,out int number) && number>0)
                        return number;
                }
        return null;
    }

    private static int? ReadDiscNumberJson(JsonElement root)
    {
        if(root.ValueKind!=JsonValueKind.Object)return null;
        if(root.TryGetProperty("format",out var format) &&
           format.ValueKind==JsonValueKind.Object &&
           format.TryGetProperty("tags",out var tags))
        {
            int? number=ReadDiscNumberTags(tags);
            if(number.HasValue)return number;
        }
        if(root.TryGetProperty("streams",out var streams) &&
           streams.ValueKind==JsonValueKind.Array)
            foreach(var stream in streams.EnumerateArray())
                if(stream.ValueKind==JsonValueKind.Object &&
                   stream.TryGetProperty("tags",out var streamTags))
                {
                    int? number=ReadDiscNumberTags(streamTags);
                    if(number.HasValue)return number;
                }
        return null;
    }

    private static int? DiscNumberAfterConversion(int? sourceDisc,bool doubleDisc)
    {
        return doubleDisc && sourceDisc.HasValue ?
            checked(sourceDisc.Value*2) : null;
    }

    private static string[] DiscMetadataArgs(int? newDisc)
    {
        if(!newDisc.HasValue)return Array.Empty<string>();
        string number=newDisc.Value.ToString(CultureInfo.InvariantCulture);
        return new[]{"-metadata","disc="+number,
                     "-metadata","discnumber="+number};
    }

    private static async Task<int?> ReadSourceDiscNumberAsync(string source,CancellationToken token)
    {
        using var p=new Process();
        p.StartInfo=CreateFfprobeStartInfo();
        foreach(string item in new[]{"-v","error","-show_entries",
            "format_tags:stream_tags","-of","json",source})
            p.StartInfo.ArgumentList.Add(item);
        if(!p.Start())throw new InvalidDataException(
            "ディスク番号を調べるためのFFprobeを起動できません");
        using var cancel=token.Register(()=>{
            try{if(!p.HasExited)p.Kill(true);}catch{}
        });
        Task<string> output=p.StandardOutput.ReadToEndAsync(token);
        Task<string> errors=p.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(output,errors,p.WaitForExitAsync(token));
        if(p.ExitCode!=0)
            throw new InvalidDataException(
                "元音源のディスク番号を確認できません: "+errors.Result.Trim());
        using var json=JsonDocument.Parse(output.Result);
        return ReadDiscNumberJson(json.RootElement);
    }

    private static async Task<string> SourceTitle(string source,CancellationToken token)
    {
        using var p = new Process();
        p.StartInfo=CreateFfprobeStartInfo();
        foreach(string x in new[]{"-v","error","-show_entries",
            "format_tags=title:stream_tags=title","-of","json",source})
            p.StartInfo.ArgumentList.Add(x);
        try
        {
            p.Start();
            // Read both pipes to prevent an unlikely ffprobe output deadlock.
            Task<string> stdout=p.StandardOutput.ReadToEndAsync(token);
            Task<string> stderr=p.StandardError.ReadToEndAsync(token);
            await Task.WhenAll(stdout,stderr,p.WaitForExitAsync(token));
            if(p.ExitCode==0)
            {
                using var doc=JsonDocument.Parse(stdout.Result);
                string? metadataTitle=ReadSourceTitleJson(doc.RootElement);
                if(!string.IsNullOrWhiteSpace(metadataTitle))
                    return Sanitize(metadataTitle);
            }
        }
        catch(OperationCanceledException){throw;}
        catch { /* a malformed/absent tag falls back to the original filename */ }
        return Sanitize(Path.GetFileNameWithoutExtension(source));
    }

    private static async Task<string> MoveOneOutput(string rendered,string original,
        string outputBase,CancellationToken token)
    {
        string dir=Path.Combine(outputBase,"3D変換先");
        Directory.CreateDirectory(dir);
        string title=await SourceTitle(original,token);
        for(int n=1;n<=10000;n++) {
            string finalName=title+(n==1?"":"("+n+")")+"_v1.0.flac";
            string dest=Path.Combine(dir,finalName);
            FileStream? writer=null;
            try {writer=new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None);}
            catch(IOException) when(File.Exists(dest)) {continue;}
            try {
                using(writer)
                using(var reader=File.OpenRead(rendered))
                    await reader.CopyToAsync(writer,1024*1024,token);
                if(new FileInfo(dest).Length<4096)throw new InvalidDataException("FLACが小さすぎます");
                return dest;
            }
            catch {
                writer?.Dispose();
                try{File.Delete(dest);}catch{}
                throw;
            }
        }
        throw new IOException("同名曲の保存番号が上限に達しました");
    }

    // Local Windows EXE smoke test; does NOT prove any real H3/FFmpeg rendering.
    // Fail-closed build gate for naming, file cleanup, stem alignment, and IR tail.
    internal static async Task SelfTestAsync()
    {
        string root=Path.Combine(Path.GetTempPath(),
            "DonOriginal3DMusic3_v1.0_selftest_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        static void Require(bool valid,string message)
        {
            if(!valid)throw new InvalidOperationException("EXE self-test: "+message);
        }
        try
        {
            string backing=Path.Combine(root,"input");
            Directory.CreateDirectory(backing);
            string rendered=Path.Combine(backing,"rendered.flac");
            await File.WriteAllBytesAsync(rendered,new byte[8192]);

            string destination=Path.Combine(root,"save");
            var cts=CancellationToken.None;
            string first=await MoveOneOutput(rendered,
                Path.Combine(root,"Cats are dangerous.flac"),destination,cts);
            string second=await MoveOneOutput(rendered,
                Path.Combine(root,"Cats are dangerous.flac"),destination,cts);
            Require(Path.GetFileName(first)=="Cats are dangerous_v1.0.flac",
                "first output naming");
            Require(Path.GetFileName(second)=="Cats are dangerous(2)_v1.0.flac",
                "duplicate output naming");
            Require(Path.GetFileName(Path.GetDirectoryName(first)!)=="3D変換先",
                "output directory naming");
            Require(Sanitize("猫:夏/曲")=="猫_夏_曲","Japanese song title preservation");
            Require(CreateFfprobeStartInfo().StandardOutputEncoding?.WebName=="utf-8",
                "FFprobe output must decode UTF-8, never Windows CP932");
            byte[] utf8Metadata=Encoding.UTF8.GetBytes(
                """{"format":{"tags":{"TITLE":"さよならエンドロール"}}}""");
            using(var japaneseTitle=JsonDocument.Parse(Encoding.UTF8.GetString(utf8Metadata)))
                Require(ReadSourceTitleJson(japaneseTitle.RootElement)=="さよならエンドロール",
                    "Japanese UTF-8 TITLE must remain intact");

            Require(Sanitize("COM9")=="_COM9","Windows reserved device protection");
            using(var tag=JsonDocument.Parse(
                """{"format":{"tags":{"DISCNUMBER":"1/3","title":"アルバム曲"}}}"""))
                Require(ReadDiscNumberJson(tag.RootElement)==1,
                    "DISCNUMBER 1/3 tag extraction");
            using(var tag=JsonDocument.Parse(
                """{"streams":[{"tags":{"disc":"2"}}]}"""))
                Require(ReadDiscNumberJson(tag.RootElement)==2,
                    "stream DISC tag extraction");
            using(var tag=JsonDocument.Parse("""{"format":{"tags":{"title":"No disc"}}}"""))
                Require(ReadDiscNumberJson(tag.RootElement)==null,
                    "missing disc remains unset");
            Require(DiscNumberAfterConversion(1,true)==2 &&
                    DiscNumberAfterConversion(2,true)==4 &&
                    DiscNumberAfterConversion(null,true)==null &&
                    DiscNumberAfterConversion(1,false)==null,
                "legacy disc-number x2 default / opt-out");
            Require(DiscMetadataArgs(2).SequenceEqual(new[]{
                "-metadata","disc=2","-metadata","discnumber=2"}),
                "FLAC disc metadata rewrite");
            float[] roomL=new float[Rate],roomR=new float[Rate];
            roomL[480]=0.8f;roomR[480]=0.8f;
            roomL[960]=0.5f;roomR[960]=0.5f;
            roomL[12000]=0.4f;roomR[12000]=0.4f;
            ScaleLateRoomReflections(roomL,roomR,45);
            Require(Math.Abs(roomL[960]-0.5f)<0.00001 &&
                    Math.Abs(roomL[12000]-0.18f)<0.00001 &&
                    Math.Abs(roomR[12000]-0.18f)<0.00001,
                "early acoustic response preserved and late room scaled");
            float[] originalTail=new float[Rate],originalTailR=new float[Rate];
            originalTail[480]=0.8f;originalTailR[480]=0.8f;
            originalTail[12000]=0.4f;originalTailR[12000]=0.4f;
            ScaleLateRoomReflections(originalTail,originalTailR,100);
            Require(originalTail[12000]==0.4f && originalTailR[12000]==0.4f,
                "100 percent preserves original room");


            using(var formatTitle=JsonDocument.Parse(
                """{"format":{"tags":{"TITLE":"Cats are dangerous"}}}"""))
                Require(ReadSourceTitleJson(formatTitle.RootElement)=="Cats are dangerous",
                    "format-level Title metadata");
            using(var streamTitle=JsonDocument.Parse(
                """{"streams":[{"tags":{"TITLE":"ストリーム曲名"}}]}"""))
                Require(ReadSourceTitleJson(streamTitle.RootElement)=="ストリーム曲名",
                    "stream-level Title metadata");
            var outputFiles=Directory.GetFiles(Path.GetDirectoryName(first)!);
            Require(outputFiles.Length==2 && outputFiles.All(f=>
                Path.GetExtension(f).Equals(".flac",StringComparison.OrdinalIgnoreCase)),
                "research debug files leaked into output");

            const int frames=96000;
            const int bytes=frames*2*sizeof(float);
            var voice=new float[frames*2];
            var bass=new float[frames*2];
            var stereo=new float[frames*2];
            for(int i=0;i<voice.Length;i++)
            {
                voice[i]=0.12f;
                bass[i]=0.08f;
                stereo[i]=0.25f;
            }
            void WriteRaw(string name,float[] values)
            {
                byte[] data=new byte[values.Length*sizeof(float)];
                Buffer.BlockCopy(values,0,data,0,data.Length);
                File.WriteAllBytes(Path.Combine(root,name),data);
            }
            WriteRaw("src.f32",stereo);WriteRaw("voice.f32",voice);
            WriteRaw("bass.f32",bass);
            ReconstructBuses(Path.Combine(root,"src.f32"),
                Path.Combine(root,"voice.f32"),
                Path.Combine(root,"bass.f32"),
                Path.Combine(root,"voice_out.f32"),
                Path.Combine(root,"bass_out.f32"),
                Path.Combine(root,"complement_out.f32"),cts);
            var residual=ReadFloat(Path.Combine(root,"complement_out.f32"));
            Require(residual.Length==frames*2 &&
                Math.Abs(residual[5] - 0.05f)<0.00001,
                "stem complement reconstruction");
            // A 0.3s short model prediction must fail, never lose vocals by padding.
            File.WriteAllBytes(Path.Combine(root,"bass_short.f32"),new byte[bytes/2]);
            bool rejected=false;
            try
            {
                ReconstructBuses(Path.Combine(root,"src.f32"),
                    Path.Combine(root,"voice.f32"),
                    Path.Combine(root,"bass_short.f32"),
                    Path.Combine(root,"voice_short_out.f32"),
                    Path.Combine(root,"bass_short_out.f32"),
                    Path.Combine(root,"back_short_out.f32"),cts);
            }
            catch(InvalidDataException){rejected=true;}
            Require(rejected,"misaligned Stem must fail");

            var left=new float[2400];var right=new float[2400];
            left[300]=0.2f;right[300]=0.3f;
            left[1000]=0.06f;right[1000]=0.04f;
            var head=new float[2400*2];
            head[300*2]=0.2f;head[300*2+1]=0.3f;
            var hybrid=ReplaceDirect(left,right,head);
            Require(hybrid.L[1000]==left[1000] &&
                hybrid.R[1000]==right[1000],
                "room reverberation tail must remain unchanged");
            string frontGraph=Filter(1,-0.5,false);
            string rearGraph=Filter(1,-0.5,true);
            Require(frontGraph.Contains("afir=irnorm=-1") &&
                    rearGraph.Contains("afir=irnorm=-1"),
                "full measured room IR not found in DSP");
            Require(frontGraph.Contains("[vFL][irFLV]") &&
                    frontGraph.Contains("[vFR][irFRV]") &&
                    rearGraph.Contains("[vFL][irRLV]") &&
                    rearGraph.Contains("[vFR][irRRV]"),
                "front/rear vocal object routing");
            // This exact class of wiring error previously caused:
            // "Filter 'asplit' has output 0 (irFLL) unconnected".
            Require(!frontGraph.Contains("[irFLL]") &&
                    !rearGraph.Contains("[irFLL]") &&
                    !frontGraph.Contains("[irFRL]") &&
                    !rearGraph.Contains("[irFRL]"),
                "unused front IR asplit pads must not exist");
            bool rejectedUnused=false;
            try
            {
                ValidateFilterGraphTopology(
                    frontGraph.Replace("[3:a]asplit=2[irFLB][irFLV]",
                                       "[3:a]asplit=3[irFLL][irFLB][irFLV]"));
            }
            catch(InvalidDataException){rejectedUnused=true;}
            Require(rejectedUnused,"dangling IR branch must fail graph gate");
            Require(frontGraph.Contains("[bF0]volume=0.1") &&
                    frontGraph.Contains("[bR0]volume=0.9"),
                "rear bass routed correctly");
        }
        finally
        {
            try{Directory.Delete(root,true);}catch { }
        }
    }

    private static async Task ValidateFlacAsync(
        string flac,long inputFrames,CancellationToken token)
    {
        // Validate decoded metadata *before* exposing a file to end users.
        using var p=new Process();
        p.StartInfo=CreateFfprobeStartInfo();
        foreach(string arg in new [] {
            "-v","error","-select_streams","a:0",
            "-show_entries","stream=codec_name,channels,sample_rate,bits_per_raw_sample:format=duration",
            "-of","json",flac
        })p.StartInfo.ArgumentList.Add(arg);
        if(!p.Start())throw new InvalidDataException("FLACの検証が開始できません");
        using var cancel=token.Register(()=>{
            try{if(!p.HasExited)p.Kill(true);}catch{}
        });
        Task<string> result=p.StandardOutput.ReadToEndAsync(token);
        Task<string> errors=p.StandardError.ReadToEndAsync(token);
        await Task.WhenAll(result,errors,p.WaitForExitAsync(token));
        if(p.ExitCode!=0)
            throw new InvalidDataException("FLAC形式検査に失敗: "+errors.Result.Trim());
        using var json=JsonDocument.Parse(result.Result);
        if(!json.RootElement.TryGetProperty("streams",out var streams) ||
           streams.GetArrayLength()!=1)
            throw new InvalidDataException("最終FLACに音声ストリームがありません");
        JsonElement first=streams[0];
        string codec=first.GetProperty("codec_name").GetString()??"";
        string hz=first.GetProperty("sample_rate").GetString()??"";
        int channels=first.GetProperty("channels").GetInt32();
        string bits=first.GetProperty("bits_per_raw_sample").GetString()??"";
        if(codec!="flac" || hz!="48000" || channels!=2 || bits!="24")
            throw new InvalidDataException(
                "配布音源はFLAC 48kHz/24bit/2chである必要があります。"
                +" 実際: "+codec+" "+hz+"Hz "+bits+"bit "+channels+"ch");
        if(!json.RootElement.TryGetProperty("format",out var format) ||
           !format.TryGetProperty("duration",out var durationElement) ||
           !double.TryParse(durationElement.GetString(),NumberStyles.Float,
               CultureInfo.InvariantCulture,out double seconds))
            throw new InvalidDataException("FLAC再生時間を確認できません");
        double sourceSeconds=inputFrames/(double)Rate;
        if(!double.IsFinite(seconds) || seconds<sourceSeconds-0.02 ||
           seconds>sourceSeconds+30.0)
            throw new InvalidDataException("最終FLACの再生時間が原曲と一致しません");
    }

    public static async Task<string> ConvertAsync(string input,string outputBase,
        double voiceDb,double backingDb,double roomPercent,
        bool vocalRear,bool doubleDiscNumber,int? manualDisc,
        IProgress<int>? progress,IProgress<string>? discProgress,
        CancellationToken token)
    {
        if(!File.Exists(input))throw new FileNotFoundException("変換元がありません",input);
        if(string.IsNullOrWhiteSpace(outputBase))throw new ArgumentException("保存先が空です");
        if(!double.IsFinite(roomPercent) || roomPercent<20 || roomPercent>100)
            throw new ArgumentOutOfRangeException(nameof(roomPercent));
        if(manualDisc.HasValue && (manualDisc.Value<1 || manualDisc.Value>99))
            throw new ArgumentOutOfRangeException(nameof(manualDisc));
        if(voiceDb<-3 || voiceDb>6 || backingDb<-4 || backingDb>3)
            throw new ArgumentOutOfRangeException("音量補正設定が範囲外です");
        string[] absent=MissingAssets().ToArray();
        if(absent.Length>0)throw new FileNotFoundException("配布素材が不足しています:\n"+string.Join("\n",absent));
        // The entire research rendering/audit lives in disposable OS temp, not 3D変換先.
        string temp=Path.Combine(Path.GetTempPath(),"DonOriginal3DMusic3_v1.0_"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try {
            string separatorInput=Path.Combine(temp,"separator.wav");
            await StemSeparatorService.CreateSeparatorInputAsync(Ff,input,separatorInput,null,null,token);
            progress?.Report(5);
            StemFiles stems=await StemSeparatorService.SeparateAsync(separatorInput,
                Path.Combine(temp,"stems"),new Progress<int>(p=>progress?.Report(5+(int)(p*.4))),null,token);
            progress?.Report(45);
            string source=Path.Combine(temp,"src.f32");
            string voc=Path.Combine(temp,"voc.f32");
            string bass=Path.Combine(temp,"bass.f32");
            await DecodeAsync(separatorInput,source,2,token);
            await DecodeAsync(stems.Vocals,voc,2,token);
            await DecodeAsync(stems.Bass,bass,2,token);
            string va=Path.Combine(temp,"va.f32"),ba=Path.Combine(temp,"ba.f32"),back=Path.Combine(temp,"back.f32");
            await Task.Run(() => ReconstructBuses(source,voc,bass,va,ba,back,token),token);
            progress?.Report(51);
            var front=await BuildHybridIr(temp,"F",0,roomPercent,token);
            var rear=await BuildHybridIr(temp,"B",180,roomPercent,token);
            var sideLeft=await BuildSideRoomIr(temp,"sideL",roomPercent,token);
            var sideRight=await BuildSideRoomIr(temp,"sideR",roomPercent,token);
            progress?.Report(59);
            var ir = new[] {front.L,front.R,rear.L,rear.R,
                sideLeft.L,sideLeft.R,sideRight.L,sideRight.R};
            string[] args = {
                "-hide_banner","-loglevel","error","-y",
                "-f","f32le","-ar","48000","-ac","2","-i",va,
                "-f","f32le","-ar","48000","-ac","2","-i",ba,
                "-f","f32le","-ar","48000","-ac","2","-i",back
            };
            var command=new List<string>(args);
            foreach(string wav in ir){command.Add("-i");command.Add(wav);}
            string scene=Path.Combine(temp,"scene.f32");
            command.AddRange(new[]{"-filter_complex",Filter(voiceDb,backingDb,vocalRear),
                "-map","[out]","-ar","48000","-ac","2","-f","f32le",scene});
            await FfmpegService.RunAsync(Ff,command,token);
            progress?.Report(81);
            var originalStats=ScanRaw(source);
            var spatial=ScanRaw(scene);
            if(spatial.Frames<originalStats.Frames || spatial.Rms<1e-10 || spatial.Peak<1e-10)
                throw new InvalidDataException("全曲の長さ/音量が不正です");
            double gain=Math.Min(originalStats.Rms*Math.Pow(10,-0.35/20)/spatial.Rms,0.84/spatial.Peak);
            if(!double.IsFinite(gain)||gain<=0)
                throw new InvalidDataException("マスター音量設定が不正です");
            string master=Path.Combine(temp,"master.f32");
            await FfmpegService.RunAsync(Ff,new[]{
                "-hide_banner","-loglevel","error","-y",
                "-f","f32le","-ar","48000","-ac","2","-i",scene,
                "-af","volume="+F(gain),"-f","f32le","-ar","48000","-ac","2",master
            },token);
            var finalStats=ScanRaw(master);
            if(finalStats.Frames<originalStats.Frames || finalStats.Peak>0.86 || finalStats.Peak<1e-10)
                throw new InvalidDataException("全曲のピーク/長さ検査に失敗");
            progress?.Report(92);
            int? convertedDiscNumber=null;
            int? originalDiscNumber=null;
            if(doubleDiscNumber)
            {
                originalDiscNumber=manualDisc ??
                    await ReadSourceDiscNumberAsync(input,token);
                convertedDiscNumber=DiscNumberAfterConversion(originalDiscNumber,true);
            }
            string tempFlac=Path.Combine(temp,"rendered.flac");
            var encodeArgs=new List<string>{
                "-hide_banner","-loglevel","error","-y",
                "-f","f32le","-ar","48000","-ac","2","-i",master,
                "-i",input,"-map","0:a:0","-map_metadata","1","-vn"
            };
            encodeArgs.AddRange(DiscMetadataArgs(convertedDiscNumber));
            encodeArgs.AddRange(new[]{
                "-c:a","flac","-sample_fmt","s32","-bits_per_raw_sample","24",
                "-ar","48000","-ac","2",tempFlac
            });
            await FfmpegService.RunAsync(Ff,encodeArgs,token);
            if(!File.Exists(tempFlac)||new FileInfo(tempFlac).Length<4096)
                throw new IOException("最終FLACの書き出しに失敗");
            await ValidateFlacAsync(tempFlac,originalStats.Frames,token);
            if(convertedDiscNumber.HasValue &&
               await ReadSourceDiscNumberAsync(tempFlac,token)!=convertedDiscNumber)
                throw new InvalidDataException(
                    "ディスク番号が変換後のFLACに反映されませんでした");
            string discResult=!doubleDiscNumber ?
                "ディスク番号: 変更なし" :
                convertedDiscNumber.HasValue ?
                    "ディスク番号: "+originalDiscNumber+" → "+
                    convertedDiscNumber+(manualDisc.HasValue?"（手動元番号）":"") :
                    "ディスク番号: 元曲に番号がありません。必要なら手動指定してください";
            discProgress?.Report(Path.GetFileName(input)+" / "+discResult);
            string result=await MoveOneOutput(tempFlac,input,outputBase,token);
            progress?.Report(100);
            return result;
        }
        finally {
            try{Directory.Delete(temp,true);}catch{}
        }
    }
}
