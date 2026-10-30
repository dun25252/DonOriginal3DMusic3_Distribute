using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DonOriginal3DMusic3;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Length==1 && args[0]=="--self-test")
        {
            try
            {
                DistributionAudioEngine.SelfTestAsync().GetAwaiter().GetResult();
                Environment.ExitCode=0;
            }
            catch(Exception ex)
            {
                // A Windows GUI executable does not have a dependable console.
                // The build script consumes exit code and a text report in its
                // own TEMP directory; this never appears in consumer exports.
                string report=Path.Combine(Path.GetTempPath(),
                    "DonOriginal3DMusic3_v1.0_selftest_failure.txt");
                try{File.WriteAllText(report,ex.ToString());}catch{}
                Environment.ExitCode=1;
            }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new DistributionForm());
    }
}

internal sealed class DistributionSettings
{
    public string OutputBase {get;set;} = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    public decimal VoiceGain {get;set;} = 1m;
    public decimal BackingGain {get;set;} = -0.5m;
    public bool DoubleDiscNumber {get;set;} = true;
    public decimal LateRoomPercent {get;set;} = 45m;
    public decimal BassPercent {get;set;} = 75m;
    public decimal VocalSpacePercent {get;set;} = 55m;
    public bool VocalRear {get;set;} = false;
    public int VocalSeat {get;set;} = -1; // carry over historical front/rear settings
    public bool ManualDiscNumber {get;set;} = false;
    public decimal DiscNumberOverride {get;set;} = 1m;
    private static string ConfigFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DonOriginal3DMusic3", "distribution_settings_v1.0.json");
    public static DistributionSettings Load()
    {
        try {
            return File.Exists(ConfigFile)
                ? JsonSerializer.Deserialize<DistributionSettings>(File.ReadAllText(ConfigFile)) ?? new()
                : new();
        } catch {return new();}
    }
    public void Save()
    {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
            File.WriteAllText(ConfigFile,JsonSerializer.Serialize(this));
        }catch { /* Settings failure must not discard music */ }
    }
}

internal sealed class DistributionForm : Form
{
    private readonly DistributionSettings _settings = DistributionSettings.Load();
    private readonly ListBox _songs = new() {
        Dock=DockStyle.Fill, SelectionMode=SelectionMode.MultiExtended,
        HorizontalScrollbar=true, IntegralHeight=false
    };
    private readonly TextBox _output = new() {Dock=DockStyle.Fill};
    private readonly NumericUpDown _vocal = new() {
        Minimum=-3,Maximum=6,DecimalPlaces=1,Increment=.5m,Width=64
    };
    private readonly NumericUpDown _backing = new() {
        Minimum=-4,Maximum=3,DecimalPlaces=1,Increment=.5m,Width=64
    };
    private readonly ComboBox _vocalPosition = new() {
        DropDownStyle=ComboBoxStyle.DropDownList,Width=110
    };
    private readonly NumericUpDown _bassPercent = new() {
        Minimum=30,Maximum=100,Increment=5,DecimalPlaces=0,Width=66,Value=75
    };
    private readonly NumericUpDown _vocalSpacePercent = new() {
        Minimum=0,Maximum=100,Increment=5,DecimalPlaces=0,Width=66,Value=55
    };
    private readonly NumericUpDown _roomPercent = new() {
        Minimum=20,Maximum=100,Increment=5,DecimalPlaces=0,Width=65,Value=45
    };
    private readonly CheckBox _manualDisc = new() {
        AutoSize=true,Text="元ディスク番号を手動指定"
    };
    private readonly NumericUpDown _manualDiscValue = new() {
        Minimum=1,Maximum=99,DecimalPlaces=0,Width=65,Enabled=false
    };
    private readonly CheckBox _doubleDisc = new() {
        AutoSize=true,Checked=true,
        Text="ディスク番号を2倍にする（旧版仕様：1→2、2→4）"
    };
    private readonly ProgressBar _bar = new() {Dock=DockStyle.Fill,Minimum=0,Maximum=100};
    private readonly Label _status = new() {
        AutoEllipsis=true, Text="変換する曲を選んでください。",Dock=DockStyle.Fill,
        TextAlign=ContentAlignment.MiddleLeft
    };
    private readonly Label _count = new() {
        AutoSize=true,Text="0曲",TextAlign=ContentAlignment.MiddleLeft
    };
    private readonly Button _add = new() {Text="曲を追加",AutoSize=true};
    private readonly Button _folder = new() {Text="フォルダーから追加",AutoSize=true};
    private readonly Button _remove = new() {Text="選択した曲を削除",AutoSize=true};
    private readonly Button _clear = new() {Text="全曲を削除",AutoSize=true};
    private readonly Button _start = new() {
        Text="選択した曲をまとめて3D変換",AutoSize=true,Height=44
    };
    private readonly Button _cancel = new() {
        Text="中止",AutoSize=true,Enabled=false,Height=44
    };
    private readonly Button _browse = new() {Text="参照...",AutoSize=true};
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _cts;
    private bool _busy;
    private static readonly HashSet<string> Extensions =
        new(new[]{".flac",".wav",".mp3",".m4a",".aac",".aiff",".aif",".ogg",".opus",".wma"},StringComparer.OrdinalIgnoreCase);

    public DistributionForm()
    {
        Text="DonOriginal3DMusic3 Ver.1.0";
        StartPosition=FormStartPosition.CenterScreen;
        MinimumSize=new Size(670,450);
        Size=new Size(900,660);
        Font=new Font("Yu Gothic UI",10f);
        AllowDrop=true;
        _output.Text=_settings.OutputBase;
        _vocal.Value=Math.Clamp(_settings.VoiceGain,_vocal.Minimum,_vocal.Maximum);
        _backing.Value=Math.Clamp(_settings.BackingGain,_backing.Minimum,_backing.Maximum);
        _doubleDisc.Checked=_settings.DoubleDiscNumber;
        _roomPercent.Value=Math.Clamp(_settings.LateRoomPercent,_roomPercent.Minimum,_roomPercent.Maximum);
        _bassPercent.Value=Math.Clamp(_settings.BassPercent,_bassPercent.Minimum,_bassPercent.Maximum);
        _vocalSpacePercent.Value=Math.Clamp(_settings.VocalSpacePercent,
            _vocalSpacePercent.Minimum,_vocalSpacePercent.Maximum);
        _vocalPosition.Items.AddRange(new object[]{
            "前方 0°","後方 180°","上前 +30°","上後 +30°","下前 -30°"
        });
        _vocalPosition.SelectedIndex=_settings.VocalSeat>=0 && _settings.VocalSeat<5 ?
            _settings.VocalSeat : (_settings.VocalRear?1:0);
        _manualDisc.Checked=_settings.ManualDiscNumber;
        _manualDiscValue.Value=Math.Clamp(_settings.DiscNumberOverride,_manualDiscValue.Minimum,_manualDiscValue.Maximum);
        _manualDiscValue.Enabled=_doubleDisc.Checked && _manualDisc.Checked;
        Controls.Add(BuildLayout());
        _add.Click+=(_,_)=>AddFiles();
        _folder.Click+=(_,_)=>AddFolder();
        _remove.Click+=(_,_)=>RemoveSelected();
        _clear.Click+=(_,_)=>{_songs.Items.Clear();_known.Clear();UpdateCount();};
        _browse.Click+=(_,_)=>BrowseOutput();
        _start.Click+=async (_,_)=>await RunBatchAsync();
        _cancel.Click+=(_,_)=>_cts?.Cancel();
        _output.Leave+=(_,_)=>SaveSettings();
        _vocal.ValueChanged+=(_,_)=>SaveSettings();
        _backing.ValueChanged+=(_,_)=>SaveSettings();
        _doubleDisc.CheckedChanged+=(_,_)=>{
            _manualDisc.Enabled=_doubleDisc.Checked;
            _manualDiscValue.Enabled=_doubleDisc.Checked && _manualDisc.Checked;
            SaveSettings();
        };
        _roomPercent.ValueChanged+=(_,_)=>SaveSettings();
        _bassPercent.ValueChanged+=(_,_)=>SaveSettings();
        _vocalSpacePercent.ValueChanged+=(_,_)=>SaveSettings();
        _vocalPosition.SelectedIndexChanged+=(_,_)=>SaveSettings();
        _manualDisc.CheckedChanged+=(_,_)=>{
            _manualDiscValue.Enabled=_doubleDisc.Checked && _manualDisc.Checked;
            SaveSettings();
        };
        _manualDiscValue.ValueChanged+=(_,_)=>SaveSettings();
        _manualDisc.Enabled=_doubleDisc.Checked;
        FormClosing+=(_,e)=>{if(_busy){e.Cancel=true;_cts?.Cancel();}else SaveSettings();};
        DragEnter+=(s,e)=>{
            e.Effect=e.Data?.GetDataPresent(DataFormats.FileDrop)==true?
                DragDropEffects.Copy:DragDropEffects.None;
        };
        DragDrop+=(s,e)=>{
            if(e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
                AddPaths(paths);
        };
    }

    private Control BuildLayout()
    {
        var root=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(16),
            ColumnCount=1,RowCount=5};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title=new Label {Text="3D音楽変換",Font=new Font(Font.FontFamily,18f,FontStyle.Bold),
            AutoSize=true,Margin=new Padding(0,0,0,10)};
        root.Controls.Add(title,0,0);
        var actions=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,
            WrapContents=true,Padding=new Padding(0,0,0,5)};
        foreach(Button b in new[]{_add,_folder,_remove,_clear})actions.Controls.Add(b);
        actions.Controls.Add(_count);
        root.Controls.Add(actions,0,1);
        root.Controls.Add(_songs,0,2);

        var bottom=new TableLayoutPanel {
            Dock=DockStyle.Fill,AutoSize=true,ColumnCount=3,RowCount=5,
            Margin=new Padding(0,10,0,6)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(new Label{Text="保存先",AutoSize=true,
            Margin=new Padding(0,7,10,4)},0,0);
        bottom.Controls.Add(_output,1,0);bottom.Controls.Add(_browse,2,0);
        var gains=new FlowLayoutPanel {AutoSize=true,WrapContents=true,Dock=DockStyle.Fill};
        gains.Controls.Add(new Label{Text="ボーカル (dB)",AutoSize=true,
            Margin=new Padding(0,6,5,0)});
        gains.Controls.Add(_vocal);
        gains.Controls.Add(new Label{Text="伴奏 (dB)",AutoSize=true,
            Margin=new Padding(18,6,5,0)});
        gains.Controls.Add(_backing);
        gains.Controls.Add(new Label {Text="ボーカル定位",AutoSize=true,
            Margin=new Padding(18,6,5,0)});
        gains.Controls.Add(_vocalPosition);
        bottom.Controls.Add(gains,0,1);bottom.SetColumnSpan(gains,3);
        var discOptions=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Dock=DockStyle.Fill};
        discOptions.Controls.Add(_doubleDisc);
        discOptions.Controls.Add(_manualDisc);
        discOptions.Controls.Add(_manualDiscValue);
        discOptions.Controls.Add(new Label{
            Text="元音源に番号がない場合は変更しません。",AutoSize=true,
            ForeColor=SystemColors.GrayText,Margin=new Padding(12,5,0,0)
        });
        bottom.Controls.Add(discOptions,0,2);bottom.SetColumnSpan(discOptions,3);
        var hint=new Label{
            Text="変換後の音源は保存先の「3D変換先」にまとめて保存されます。",
            AutoSize=true,ForeColor=SystemColors.GrayText,
            Margin=new Padding(0,4,0,5)
        };
        var roomOptions=new FlowLayoutPanel{AutoSize=true,WrapContents=true,Dock=DockStyle.Fill};
        roomOptions.Controls.Add(new Label{Text="室内反射 (%)",AutoSize=true,
            Margin=new Padding(0,6,5,0)});
        roomOptions.Controls.Add(_roomPercent);
        roomOptions.Controls.Add(new Label{Text="低音の量 (%)",AutoSize=true,
            Margin=new Padding(12,6,5,0)});
        roomOptions.Controls.Add(_bassPercent);
        roomOptions.Controls.Add(new Label{Text="歌声の空間感 (%)",AutoSize=true,
            Margin=new Padding(12,6,5,0)});
        roomOptions.Controls.Add(_vocalSpacePercent);
        bottom.Controls.Add(roomOptions,0,3);bottom.SetColumnSpan(roomOptions,3);
        bottom.Controls.Add(hint,0,4);bottom.SetColumnSpan(hint,3);
        root.Controls.Add(bottom,0,3);

        var progress=new TableLayoutPanel {Dock=DockStyle.Fill,AutoSize=true,
            RowCount=3,ColumnCount=1};
        progress.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        progress.Controls.Add(_bar,0,0);
        progress.Controls.Add(_status,0,1);
        var buttons=new FlowLayoutPanel {AutoSize=true,Dock=DockStyle.Fill,
            FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        buttons.Controls.Add(_cancel);buttons.Controls.Add(_start);
        progress.Controls.Add(buttons,0,2);
        root.Controls.Add(progress,0,4);
        return root;
    }

    private void SaveSettings()
    {
        _settings.OutputBase=_output.Text.Trim();
        _settings.VoiceGain=_vocal.Value;
        _settings.BackingGain=_backing.Value;
        _settings.DoubleDiscNumber=_doubleDisc.Checked;
        _settings.LateRoomPercent=_roomPercent.Value;
        _settings.BassPercent=_bassPercent.Value;
        _settings.VocalSpacePercent=_vocalSpacePercent.Value;
        _settings.VocalRear=_vocalPosition.SelectedIndex==1;
        _settings.VocalSeat=_vocalPosition.SelectedIndex;
        _settings.ManualDiscNumber=_manualDisc.Checked;
        _settings.DiscNumberOverride=_manualDiscValue.Value;
        _settings.Save();
    }

    private void AddFiles()
    {
        using var d=new OpenFileDialog {
            Title="3D変換する音楽を選択",Multiselect=true,CheckFileExists=true,
            Filter="音楽ファイル|*.flac;*.wav;*.mp3;*.m4a;*.aac;*.aiff;*.aif;*.ogg;*.opus;*.wma|すべてのファイル|*.*"
        };
        if(d.ShowDialog(this)==DialogResult.OK)AddPaths(d.FileNames);
    }

    private void AddFolder()
    {
        using var d=new FolderBrowserDialog {Description="音楽ファイルが入ったフォルダーを選択"};
        if(d.ShowDialog(this)==DialogResult.OK)AddPaths(new[]{d.SelectedPath});
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        if(_busy)return;
        foreach(string selected in paths)
        {
            if(File.Exists(selected)) {AddFile(selected);continue;}
            if(!Directory.Exists(selected))continue;
            try {
                foreach(string f in Directory.EnumerateFiles(selected,"*",SearchOption.AllDirectories))
                    AddFile(f);
            } catch(Exception ex) {
                MessageBox.Show(this,selected+"\n"+ex.Message,"フォルダーの読み込み",
                    MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }
        UpdateCount();
    }

    private void AddFile(string file)
    {
        if(!Extensions.Contains(Path.GetExtension(file)))return;
        string abs=Path.GetFullPath(file);
        if(_known.Add(abs))_songs.Items.Add(abs);
    }

    private void RemoveSelected()
    {
        if(_busy)return;
        foreach(var obj in _songs.SelectedItems.Cast<object>().ToArray())
        {
            if(obj is string x)_known.Remove(x);
            _songs.Items.Remove(obj);
        }
        UpdateCount();
    }

    private void UpdateCount() => _count.Text=_songs.Items.Count+"曲";

    private void BrowseOutput()
    {
        using var d=new FolderBrowserDialog {
            Description="保存先フォルダーを選択",
            SelectedPath=Directory.Exists(_output.Text)?_output.Text:""
        };
        if(d.ShowDialog(this)==DialogResult.OK) {
            _output.Text=d.SelectedPath;
            SaveSettings();
        }
    }

    private void SetBusy(bool value)
    {
        _busy=value;
        foreach(Control c in new Control[]{_add,_folder,_remove,_clear,_start,
            _browse,_songs,_output,_vocal,_backing,_doubleDisc,_roomPercent,
            _bassPercent,_vocalSpacePercent,_vocalPosition,
            _manualDisc,_manualDiscValue})c.Enabled=!value;
        if(!value) {
            _manualDisc.Enabled=_doubleDisc.Checked;
            _manualDiscValue.Enabled=_doubleDisc.Checked && _manualDisc.Checked;
        }
        _cancel.Enabled=value;
    }

    private async Task RunBatchAsync()
    {
        if(_busy)return;
        string[] paths=_songs.Items.Cast<string>().ToArray();
        if(paths.Length==0) {
            MessageBox.Show(this,"変換する曲を追加してください。","3D音楽変換",
                MessageBoxButtons.OK,MessageBoxIcon.Information);return;
        }
        string output=_output.Text.Trim();
        if(string.IsNullOrWhiteSpace(output)) {
            MessageBox.Show(this,"保存先を選択してください。","3D音楽変換",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);return;
        }
        var missing=DistributionAudioEngine.MissingAssets();
        if(missing.Count>0) {
            MessageBox.Show(this,
                "必要な音響データが見つかりません。配布ZIPを最後まで展開してください。\n\n"+
                    string.Join("\n",missing.Take(8)),
                "配布素材が不足",MessageBoxButtons.OK,MessageBoxIcon.Error);return;
        }
        SaveSettings();
        SetBusy(true);
        _cts=new CancellationTokenSource();
        int succeeded=0;var failures=new List<string>();
        var discReports=new List<string>();
        _bar.Value=0;
        try {
            for(int i=0;i<paths.Length;i++) {
                if(_cts.IsCancellationRequested)break;
                int index=i;
                string file=paths[i];
                _status.Text=(i+1)+"/"+paths.Length+"  "+Path.GetFileName(file);
                var progress=new Progress<int>(p=>{
                    int percent=(int)((index+p/100d)*100/paths.Length);
                    _bar.Value=Math.Clamp(percent,0,100);
                });
                try {
                    var discProgress=new Progress<string>(message=>{
                        _status.Text=message;
                        discReports.Add(message);
                    });
                    await DistributionAudioEngine.ConvertAsync(
                        file,output,(double)_vocal.Value,(double)_backing.Value,
                        (double)_roomPercent.Value,_vocalPosition.SelectedIndex,
                        (int)_bassPercent.Value,(int)_vocalSpacePercent.Value,
                        _doubleDisc.Checked,
                        _manualDisc.Checked ? (int)_manualDiscValue.Value : null,
                        progress,discProgress,_cts.Token);
                    succeeded++;
                }catch(OperationCanceledException){break;}
                catch(Exception ex){failures.Add(Path.GetFileName(file)+": "+ex.Message);}
            }
            _bar.Value=(int)(100.0*(succeeded+failures.Count)/paths.Length);
            _status.Text="完了 "+succeeded+"曲、失敗 "+failures.Count+"曲"+
                (_cts.IsCancellationRequested?"（中止）":"");
            if(failures.Count>0)MessageBox.Show(this,
                string.Join("\n\n",failures.Take(10)), "変換できなかった曲",
                MessageBoxButtons.OK,MessageBoxIcon.Warning);
            else if(!_cts.IsCancellationRequested)
                MessageBox.Show(this,succeeded+"曲を変換しました。\n\n"+
                    (discReports.Count==0?"":string.Join("\n",discReports.Take(8))+"\n\n")+
                    Path.Combine(output,"3D変換先"),"3D音楽変換",
                    MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        finally {
            _cts.Dispose();_cts=null;SetBusy(false);
        }
    }
}
