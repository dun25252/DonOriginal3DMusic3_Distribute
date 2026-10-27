using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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

    private static async Task<(string L,string R)> BuildHybridIr(
        string temp, string direction, int angle, CancellationToken token)
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

    private static string Filter(double voiceDb,double backingDb)
    {
        double vg=Math.Pow(10,voiceDb/20),ig=Math.Pow(10,backingDb/20);
        return
            "[0:a]aformat=channel_layouts=stereo,asplit=2[v0][vs0];"+
            "[v0]pan=mono|c0=0.5*c0+0.5*c1,volume="+F(vg)+",asplit=2[vFL][vFR];"+
            "[vs0]pan=mono|c0=0.5*c0-0.5*c1,volume="+F(vg)+"[vSide];"+
            "[1:a]aformat=channel_layouts=stereo,pan=mono|c0=0.5*c0+0.5*c1,asplit=2[bF0][bR0];"+
            "[bF0]volume=0.72,asplit=2[bFL][bFR];"+
            "[bR0]volume=0.28,asplit=2[bRL][bRR];"+
            "[2:a]aformat=channel_layouts=stereo,asplit=2[backM0][backS0];"+
            "[backM0]pan=mono|c0=0.5*c0+0.5*c1,volume="+F(ig)+",asplit=2[backRL][backRR];"+
            "[backS0]pan=mono|c0=0.5*c0-0.5*c1,volume="+F(ig)+"[backSide];"+
            "[vSide][backSide]amix=inputs=2:normalize=0:duration=longest,asplit=2[sL0][sR0];"+
            "[sL0]asplit=2[sLL][sLR];"+
            "[sR0]volume=-1,asplit=2[sRL][sRR];"+
            "[3:a]asplit=2[irFLL][irFLB];"+
            "[4:a]asplit=2[irFRL][irFRB];"+
            "[5:a]asplit=2[irRLL][irRLB];"+
            "[6:a]asplit=2[irRRL][irRRB];"+
            "[vFL][irFLL]"+Afir+"[vOL];"+
            "[vFR][irFRL]"+Afir+"[vOR];"+
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

    private static async Task<string> SourceTitle(string source,CancellationToken token)
    {
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo {
            FileName=Fp, WorkingDirectory=AppContext.BaseDirectory,
            RedirectStandardOutput=true,RedirectStandardError=true,
            UseShellExecute=false,CreateNoWindow=true
        };
        foreach(string x in new[]{"-v","error","-show_entries",
            "format_tags=title:stream_tags=title","-of","json",source})
            p.StartInfo.ArgumentList.Add(x);
        try {
            p.Start();
            string text=await p.StandardOutput.ReadToEndAsync(token);
            await p.WaitForExitAsync(token);
            if(p.ExitCode==0) {
                using var doc=JsonDocument.Parse(text);
                if(doc.RootElement.TryGetProperty("format",out var form) &&
                   form.TryGetProperty("tags",out var tags)) {
                    foreach(var x in tags.EnumerateObject())
                        if(x.Name.Equals("title",StringComparison.OrdinalIgnoreCase) &&
                           x.Value.ValueKind==JsonValueKind.String &&
                           !string.IsNullOrWhiteSpace(x.Value.GetString()))
                            return Sanitize(x.Value.GetString()!);
                }
            }
        }catch{ /* fallback to original basename */ }
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
            Require(Sanitize("COM9")=="_COM9","Windows reserved device protection");
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
            Require(Filter(1,-0.5).Contains("afir=irnorm=-1"),
                "full measured room IR not found in DSP");
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
        p.StartInfo=new ProcessStartInfo {
            FileName=Fp,WorkingDirectory=AppContext.BaseDirectory,
            RedirectStandardOutput=true,RedirectStandardError=true,
            UseShellExecute=false,CreateNoWindow=true
        };
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
        double voiceDb,double backingDb,IProgress<int>? progress,CancellationToken token)
    {
        if(!File.Exists(input))throw new FileNotFoundException("変換元がありません",input);
        if(string.IsNullOrWhiteSpace(outputBase))throw new ArgumentException("保存先が空です");
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
            var front=await BuildHybridIr(temp,"F",0,token);
            var rear=await BuildHybridIr(temp,"B",180,token);
            progress?.Report(59);
            var ir = new[] {front.L,front.R,rear.L,rear.R,
                Ir("F","sideL_room","L"),Ir("F","sideL_room","R"),
                Ir("F","sideR_room","L"),Ir("F","sideR_room","R")};
            string[] args = {
                "-hide_banner","-loglevel","error","-y",
                "-f","f32le","-ar","48000","-ac","2","-i",va,
                "-f","f32le","-ar","48000","-ac","2","-i",ba,
                "-f","f32le","-ar","48000","-ac","2","-i",back
            };
            var command=new List<string>(args);
            foreach(string wav in ir){command.Add("-i");command.Add(wav);}
            string scene=Path.Combine(temp,"scene.f32");
            command.AddRange(new[]{"-filter_complex",Filter(voiceDb,backingDb),
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
            string tempFlac=Path.Combine(temp,"rendered.flac");
            await FfmpegService.RunAsync(Ff,new[]{
                "-hide_banner","-loglevel","error","-y",
                "-f","f32le","-ar","48000","-ac","2","-i",master,
                "-i",input,"-map","0:a:0","-map_metadata","1","-vn",
                "-c:a","flac","-sample_fmt","s32","-bits_per_raw_sample","24",
                "-ar","48000","-ac","2",tempFlac
            },token);
            if(!File.Exists(tempFlac)||new FileInfo(tempFlac).Length<4096)
                throw new IOException("最終FLACの書き出しに失敗");
            await ValidateFlacAsync(tempFlac,originalStats.Frames,token);
            string result=await MoveOneOutput(tempFlac,input,outputBase,token);
            progress?.Report(100);
            return result;
        }
        finally {
            try{Directory.Delete(temp,true);}catch{}
        }
    }
}
