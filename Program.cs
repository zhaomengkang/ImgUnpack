using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ImgUnpack
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        internal static List<Part> FindParts(string directory)
        {
            var result = new List<Part>();
            var pattern = new Regex("^FROM(\\d+)\\.IMG$", RegexOptions.IgnoreCase);
            foreach (string path in Directory.GetFiles(directory))
            {
                Match match = pattern.Match(Path.GetFileName(path));
                int number;
                if (match.Success && int.TryParse(match.Groups[1].Value, out number))
                    result.Add(new Part(number, path, new FileInfo(path).Length));
            }
            return result.OrderBy(x => x.Number).ToList();
        }

        internal static void ExtractFrom(string sourceDirectory, string outputDirectory, Action<string> report)
        {
            List<Part> parts = FindParts(sourceDirectory);
            if (parts.Count == 0) throw new InvalidDataException("没有找到 FROM*.IMG 分片。");
            if (parts[0].Number != 0) throw new InvalidDataException("分片序列应从 FROM00.IMG 开始；当前首片为 " + parts[0].FileName + "。");
            for (int i = 1; i < parts.Count; i++)
                if (parts[i].Number != parts[i - 1].Number + 1)
                    throw new InvalidDataException("FROM 分片编号缺失：" + parts[i - 1].FileName + " 后缺少 FROM" + (parts[i - 1].Number + 1).ToString("00") + ".IMG。请补齐分片后再解包。");

            Directory.CreateDirectory(outputDirectory);
            string imagePath = Path.Combine(outputDirectory, "FROM.img");
            report("正在按编号顺序合并 " + parts.Count + " 个分片…");
            using (var output = new FileStream(imagePath, FileMode.Create, FileAccess.Write, FileShare.None))
                foreach (Part part in parts)
                {
                    using (var input = File.OpenRead(part.Path)) input.CopyTo(output);
                    report("已读取 " + part.FileName);
                }

            report("正在读取 FAT16 目录并提取文件…");
            int extracted = Fat16.Extract(imagePath, outputDirectory);
            File.Delete(imagePath);
            report("解包完成，共提取 " + extracted + " 个文件。");
            report("临时合并镜像 FROM.img 已删除。");
        }
    }

    internal sealed class Part
    {
        public int Number { get; private set; }
        public string Path { get; private set; }
        public long Length { get; private set; }
        public string FileName { get { return System.IO.Path.GetFileName(Path); } }
        public Part(int number, string path, long length) { Number = number; Path = path; Length = length; }
    }

    internal sealed class MainForm : Form
    {
        private readonly TextBox inputPath = new TextBox();
        private readonly TextBox outputPath = new TextBox();
        private readonly Label scanSummary = new Label();
        private readonly TextBox log = new TextBox();
        private readonly Button scanButton = new Button();
        private readonly Button extractButton = new Button();
        private readonly Button openButton = new Button();
        private readonly ProgressBar progress = new ProgressBar();

        private static readonly Color Ink = Color.FromArgb(29, 41, 57);
        private static readonly Color Muted = Color.FromArgb(102, 116, 135);
        private static readonly Color Blue = Color.FromArgb(33, 104, 190);

        public MainForm()
        {
            Text = "FROM 镜像解包工具";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(700, 510);
            Size = new Size(850, 610);
            BackColor = Color.FromArgb(246, 248, 251);
            Font = new Font("Microsoft YaHei UI", 9F);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var header = new Panel { Dock = DockStyle.Fill };
            header.Controls.Add(new Label { Text = "FROM 镜像解包", Font = new Font(Font, FontStyle.Bold), ForeColor = Ink, AutoSize = true, Location = new Point(0, 2) });
            header.Controls.Add(new Label { Text = "选择 FROM*.IMG 所在目录，按编号重组 FAT16 镜像并提取卷内文件。", ForeColor = Muted, AutoSize = true, Location = new Point(1, 34) });
            root.Controls.Add(header, 0, 0);

            var paths = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, BackColor = Color.White, Padding = new Padding(14, 12, 14, 12) };
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            paths.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            paths.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.Controls.Add(paths, 0, 1);

            paths.Controls.Add(MakeLabel("输入目录"), 0, 0);
            inputPath.Dock = DockStyle.Fill;
            paths.Controls.Add(inputPath, 1, 0);
            var inputBrowse = MakeButton("选择目录…", false);
            inputBrowse.Click += (s, e) => Browse(inputPath, false);
            paths.Controls.Add(inputBrowse, 2, 0);

            paths.Controls.Add(MakeLabel("输出目录"), 0, 1);
            outputPath.Dock = DockStyle.Fill;
            paths.Controls.Add(outputPath, 1, 1);
            var outputBrowse = MakeButton("选择目录…", false);
            outputBrowse.Click += (s, e) => Browse(outputPath, true);
            paths.Controls.Add(outputBrowse, 2, 1);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 10, 0, 0) };
            scanButton.Text = "扫描分片"; scanButton.Size = new Size(112, 36); scanButton.Click += (s, e) => Scan();
            extractButton.Text = "开始解包"; extractButton.Size = new Size(128, 36); extractButton.BackColor = Blue; extractButton.ForeColor = Color.White; extractButton.FlatStyle = FlatStyle.Flat; extractButton.FlatAppearance.BorderSize = 0; extractButton.Click += async (s, e) => await Extract();
            openButton.Text = "打开输出目录"; openButton.Size = new Size(132, 36); openButton.Enabled = false; openButton.Click += (s, e) => OpenOutput();
            actions.Controls.Add(scanButton); actions.Controls.Add(extractButton); actions.Controls.Add(openButton);
            root.Controls.Add(actions, 0, 2);

            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            scanSummary.Text = "尚未扫描分片"; scanSummary.ForeColor = Muted; scanSummary.TextAlign = ContentAlignment.MiddleLeft; scanSummary.Dock = DockStyle.Fill;
            status.Controls.Add(scanSummary, 0, 0);
            progress.Dock = DockStyle.Fill; progress.Style = ProgressBarStyle.Blocks; status.Controls.Add(progress, 1, 0);
            root.Controls.Add(status, 0, 3);

            log.Multiline = true; log.ReadOnly = true; log.ScrollBars = ScrollBars.Vertical; log.Dock = DockStyle.Fill; log.BorderStyle = BorderStyle.FixedSingle; log.BackColor = Color.White;
            root.Controls.Add(log, 0, 4);

            string initial = FindInitialDirectory();
            inputPath.Text = initial;
            outputPath.Text = Path.Combine(initial, "FROM_unpacked");
            Scan();
        }

        private static string FindInitialDirectory()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null)
            {
                try { if (Program.FindParts(directory.FullName).Count > 0) return directory.FullName; }
                catch { }
                directory = directory.Parent;
            }
            return Environment.CurrentDirectory;
        }

        private static Label MakeLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink };
        }

        private static Button MakeButton(string text, bool primary)
        {
            return new Button { Text = text, Dock = DockStyle.Fill, FlatStyle = primary ? FlatStyle.Flat : FlatStyle.Standard };
        }

        private void Browse(TextBox target, bool output)
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(target.Text) ? target.Text : Environment.CurrentDirectory, ShowNewFolderButton = output })
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    target.Text = dialog.SelectedPath;
                    if (target == inputPath) { outputPath.Text = Path.Combine(dialog.SelectedPath, "FROM_unpacked"); Scan(); }
                }
        }

        private void Scan()
        {
            try
            {
                if (!Directory.Exists(inputPath.Text)) throw new DirectoryNotFoundException("请选择有效的输入目录。");
                List<Part> parts = Program.FindParts(inputPath.Text);
                if (parts.Count == 0)
                {
                    scanSummary.Text = "未找到 FROM*.IMG 分片";
                    extractButton.Enabled = false;
                    return;
                }
                long total = parts.Sum(x => x.Length);
                bool missing = false;
                for (int i = 1; i < parts.Count; i++) if (parts[i].Number != parts[i - 1].Number + 1) missing = true;
                scanSummary.Text = parts.Count + " 个分片  ·  " + (total / 1024.0 / 1024.0).ToString("0.##") + " MiB  ·  " + parts[0].FileName + " 至 " + parts[parts.Count - 1].FileName + (missing ? "  ·  编号有缺失" : "");
                extractButton.Enabled = !missing;
                log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  扫描完成：" + scanSummary.Text + Environment.NewLine);
            }
            catch (Exception ex)
            {
                scanSummary.Text = ex.Message;
                extractButton.Enabled = false;
            }
        }

        private async Task Extract()
        {
            if (!Directory.Exists(inputPath.Text)) { Scan(); return; }
            if (string.IsNullOrWhiteSpace(outputPath.Text)) { MessageBox.Show(this, "请选择输出目录。", "缺少输出目录", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            string source = inputPath.Text, destination = outputPath.Text;
            SetBusy(true);
            WriteLog("开始解包 FROM 镜像…");
            try
            {
                await Task.Run(() => Program.ExtractFrom(source, destination, WriteLog));
                openButton.Enabled = true;
                MessageBox.Show(this, "FROM 镜像已成功提取到：\r\n" + destination, "解包完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                WriteLog("解包失败：" + ex.Message);
                MessageBox.Show(this, ex.Message, "解包失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { SetBusy(false); }
        }

        private void WriteLog(string message)
        {
            if (InvokeRequired) { BeginInvoke((Action)(() => WriteLog(message))); return; }
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
        }

        private void SetBusy(bool busy)
        {
            scanButton.Enabled = !busy; extractButton.Enabled = !busy; inputPath.Enabled = !busy; outputPath.Enabled = !busy;
            progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Blocks;
            if (!busy) Scan();
        }

        private void OpenOutput()
        {
            if (Directory.Exists(outputPath.Text))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = outputPath.Text, UseShellExecute = true });
        }
    }

    internal static class Fat16
    {
        private sealed class Entry
        {
            public string Name;
            public bool IsDirectory;
            public ushort Cluster;
            public uint Size;
        }

        public static int Extract(string imagePath, string destination)
        {
            byte[] image = File.ReadAllBytes(imagePath);
            if (image.Length < 512 || image[510] != 0x55 || image[511] != 0xaa) throw new InvalidDataException("FROM 镜像缺少有效 FAT 引导扇区标记。");
            int bytesPerSector = U16(image, 11), sectorsPerCluster = image[13], reserved = U16(image, 14), fatCount = image[16], rootEntries = U16(image, 17), sectorsPerFat = U16(image, 22);
            uint totalSectors = (uint)U16(image, 19); if (totalSectors == 0) totalSectors = U32(image, 32);
            if (bytesPerSector < 512 || sectorsPerCluster == 0 || fatCount == 0 || sectorsPerFat == 0 || totalSectors == 0) throw new InvalidDataException("FAT 参数无效。");
            if ((long)bytesPerSector * totalSectors > image.Length) throw new InvalidDataException("分片不完整：镜像长度小于 FAT 卷声明长度。");
            int rootSectors = (rootEntries * 32 + bytesPerSector - 1) / bytesPerSector;
            int rootSector = reserved + fatCount * sectorsPerFat;
            int dataSector = rootSector + rootSectors;
            byte[] fat = Slice(image, (long)reserved * bytesPerSector, (long)sectorsPerFat * bytesPerSector);
            var extracted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ExtractDirectory(image, fat, destination, "", rootSector, rootSectors, bytesPerSector, sectorsPerCluster, dataSector, extracted);
            return extracted.Count;
        }

        private static void ExtractDirectory(byte[] image, byte[] fat, string root, string relative, int sector, int sectors, int bps, int spc, int dataSector, HashSet<string> extracted)
        {
            byte[] bytes;
            if (relative.Length == 0) bytes = Slice(image, (long)sector * bps, (long)sectors * bps);
            else bytes = ReadClusterChain(image, fat, checked((ushort)(((sector - dataSector) / spc) + 2)), bps, spc, dataSector);
            var entries = new List<Entry>();
            var lfn = new List<string>();
            for (int pos = 0; pos + 32 <= bytes.Length; pos += 32)
            {
                int first = bytes[pos];
                if (first == 0) break;
                if (first == 0xe5) { lfn.Clear(); continue; }
                int attr = bytes[pos + 11];
                if (attr == 0x0f)
                {
                    int order = bytes[pos] & 0x1f;
                    while (lfn.Count < order) lfn.Add(null);
                    if (order > 0) lfn[order - 1] = ReadLfn(bytes, pos);
                    continue;
                }
                string name = lfn.Count > 0 && lfn.All(x => x != null) ? string.Concat(lfn) : ShortName(bytes, pos);
                lfn.Clear();
                if (name == "." || name == ".." || (attr & 0x08) != 0) continue;
                entries.Add(new Entry { Name = name, IsDirectory = (attr & 0x10) != 0, Cluster = (ushort)U16(bytes, pos + 26), Size = U32(bytes, pos + 28) });
            }

            foreach (Entry entry in entries)
            {
                string relativePath = Path.Combine(relative, SafeName(entry.Name));
                string fullPath = Path.Combine(root, relativePath);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(fullPath);
                    if (entry.Cluster >= 2) ExtractDirectory(image, fat, root, relativePath, ClusterSector(entry.Cluster, spc, dataSector), spc, bps, spc, dataSector, extracted);
                }
                else if (entry.Size > 0)
                {
                    byte[] content = ReadFile(image, fat, entry, bps, spc, dataSector);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllBytes(fullPath, content);
                    extracted.Add(relativePath);
                }
            }
        }

        private static byte[] ReadFile(byte[] image, byte[] fat, Entry entry, int bps, int spc, int dataSector)
        {
            byte[] data = ReadClusterChain(image, fat, entry.Cluster, bps, spc, dataSector);
            if (data.Length < entry.Size) throw new InvalidDataException("文件簇链提前结束：" + entry.Name);
            Array.Resize(ref data, checked((int)entry.Size));
            return data;
        }

        private static byte[] ReadClusterChain(byte[] image, byte[] fat, ushort first, int bps, int spc, int dataSector)
        {
            using (var output = new MemoryStream())
            {
                ushort cluster = first;
                var seen = new HashSet<ushort>();
                while (cluster >= 2 && cluster < 0xfff8)
                {
                    if (!seen.Add(cluster)) throw new InvalidDataException("FAT 簇链出现循环。");
                    byte[] block = Slice(image, (long)ClusterSector(cluster, spc, dataSector) * bps, (long)spc * bps);
                    output.Write(block, 0, block.Length);
                    int offset = cluster * 2;
                    if (offset + 1 >= fat.Length) throw new InvalidDataException("FAT 簇号越界。");
                    cluster = (ushort)(fat[offset] | fat[offset + 1] << 8);
                }
                return output.ToArray();
            }
        }

        private static int ClusterSector(ushort cluster, int spc, int dataSector) { return checked(dataSector + (cluster - 2) * spc); }
        private static string ShortName(byte[] data, int pos)
        {
            string name = Encoding.ASCII.GetString(data, pos, 8).TrimEnd();
            string extension = Encoding.ASCII.GetString(data, pos + 8, 3).TrimEnd();
            if (name.Length > 0 && name[0] == '\u0005') name = "\u00e5" + name.Substring(1);
            return extension.Length == 0 ? name : name + "." + extension;
        }
        private static string ReadLfn(byte[] data, int pos)
        {
            int[] offsets = { 1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30 };
            var chars = new List<char>();
            foreach (int offset in offsets) { int c = U16(data, pos + offset); if (c == 0 || c == 0xffff) break; chars.Add((char)c); }
            return new string(chars.ToArray());
        }
        private static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
        private static byte[] Slice(byte[] data, long offset, long length)
        {
            if (offset < 0 || length < 0 || offset + length > data.Length || length > int.MaxValue) throw new InvalidDataException("镜像中的扇区/簇范围越界。");
            var result = new byte[(int)length]; Buffer.BlockCopy(data, (int)offset, result, 0, (int)length); return result;
        }
        private static int U16(byte[] data, int pos) { return data[pos] | data[pos + 1] << 8; }
        private static uint U32(byte[] data, int pos) { return (uint)(data[pos] | data[pos + 1] << 8 | data[pos + 2] << 16 | data[pos + 3] << 24); }
    }
}
