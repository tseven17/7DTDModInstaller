// Program.cs – t7's 7DTD Mod-Installer (robust icon load, async + progress)
// Build/publish (framework-dependent):
// dotnet publish -c Release -r win-x64 -o publish -p:SelfContained=false -p:PublishSingleFile=false

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ModInstaller7DTD
{
    public class InstallerForm : Form
    {
        // ─── constants ───────────────────────────────────────────────────
        private const string VANILLA_HINT = @"C:\Steam\steamapps\common\";
        private const string EXE_NAME     = "7DaysToDie.exe";
        private const string MODS_FOLDER  = "Mods";
        private const string HARMONY_DIR  = "0_TFP_Harmony";
        private const string MANIFEST_FN  = "manifest7dtm.json";

        // ─── state ───────────────────────────────────────────────────────
        private string gameDir = string.Empty;
        private string modsDir = string.Empty;

        // ─── ui ──────────────────────────────────────────────────────────
        private readonly TextBox logBox;
        private readonly ProgressBar bar;
        private readonly Label barLabel;

        public InstallerForm()
        {
            Text = "t7's 7DTD Mod-Installer";

            // robust icon load
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
            if (File.Exists(icoPath))
            {
                try { Icon = new Icon(icoPath); }
                catch { /* ignore – fallback to default icon */ }
            }

            ClientSize      = new Size(760, 420);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox     = false;

            var btnPick    = MakeButton("① Pick 7DaysToDie.exe", 10, 10, PickExe);
            var btnBackup  = MakeButton("② Backup Mods",         10, 55, BackupMods);
            var btnInstall = MakeButton("③ Install Mods (ZIP)…", 10, 100, InstallMods);
            var btnRestore = MakeButton("④ Restore Backup…",     10, 145, RestoreBackup);
            var btnVerify  = MakeButton("⑤ Verify Integrity",    10, 190, VerifyIntegrity);

            Controls.AddRange(new Control[] { btnPick, btnBackup, btnInstall, btnRestore, btnVerify });

            logBox = new TextBox
            {
                Multiline = true,
                ReadOnly  = true,
                ScrollBars = ScrollBars.Vertical,
                Location  = new Point(260, 10),
                Size      = new Size(480, 340),
                Font      = new Font("Consolas", 9f)
            };
            Controls.Add(logBox);

            bar = new ProgressBar
            {
                Location = new Point(10, 250),
                Size     = new Size(730, 25),
                Minimum  = 0,
                Maximum  = 100
            };
            barLabel = new Label
            {
                Location = new Point(10, 280),
                Size     = new Size(730, 20),
                Text     = ""
            };
            Controls.Add(bar);
            Controls.Add(barLabel);

            Log("Ready. Pick your 7DaysToDie.exe to begin.");
        }

        // ─── helpers ─────────────────────────────────────────────────────
        private Button MakeButton(string text, int x, int y, EventHandler click)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(220, 35) };
            b.Click += click;
            return b;
        }

        private void Log(string msg) =>
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");

        private void SetProgress(int pct, string? txt = null)
        {
            if (InvokeRequired) { Invoke(new Action<int, string?>(SetProgress), pct, txt); return; }
            bar.Value = Math.Clamp(pct, 0, 100);
            if (txt != null) barLabel.Text = txt;
        }

        private void ToggleButtons(bool en) =>
            Controls.OfType<Button>().ToList().ForEach(b => b.Enabled = en);

        private bool CheckGameDir()
        {
            if (string.IsNullOrEmpty(gameDir))
            {
                MessageBox.Show("Pick your 7DaysToDie.exe first.", "Game folder not set",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        // ─── ① Pick EXE ────────────────────────────────────────────────
        private void PickExe(object? _, EventArgs __)
        {
            using var dlg = new OpenFileDialog
            {
                Title            = "Locate 7DaysToDie.exe",
                Filter           = "7DTD executable|7DaysToDie.exe",
                InitialDirectory = VANILLA_HINT
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            if (!Path.GetFileName(dlg.FileName).Equals(EXE_NAME, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("That isn’t 7DaysToDie.exe.", "Wrong file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            gameDir = Path.GetDirectoryName(dlg.FileName)!;
            modsDir = Path.Combine(gameDir, MODS_FOLDER);
            Directory.CreateDirectory(modsDir);

            Text = $"t7's 7DTD Mod-Installer  •  {gameDir}";
            Log($"Game folder set to: {gameDir}");

            Process.Start("explorer.exe", gameDir);
        }

        // ─── ② Backup Mods ──────────────────────────────────────────────
        private async void BackupMods(object? _, EventArgs __)
        {
            if (!CheckGameDir()) return;

            var srcFolders = Directory.EnumerateDirectories(modsDir)
                                      .Where(d => !Path.GetFileName(d).Equals(HARMONY_DIR, StringComparison.OrdinalIgnoreCase))
                                      .ToList();
            if (srcFolders.Count == 0)
            {
                Log("No mods (other than Harmony) found – nothing to back up.");
                return;
            }

            ToggleButtons(false);

            await Task.Run(() =>
            {
                string stamp      = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string backupRoot = Path.Combine(gameDir, $"ModsBackup_{stamp}");
                string backupMods = Path.Combine(backupRoot, MODS_FOLDER);
                Directory.CreateDirectory(backupMods);

                long total = srcFolders.SelectMany(f => Directory.GetFiles(f, "*", SearchOption.AllDirectories))
                                       .Sum(f => new FileInfo(f).Length);
                long copied = 0;

                foreach (var folder in srcFolders)
                {
                    string dest = Path.Combine(backupMods, Path.GetFileName(folder));
                    CopyDirectoryWithProgress(folder, dest, ref copied, total, "Backing-up");
                    Directory.Delete(folder, true);
                    Log($"Backed up {Path.GetFileName(folder)}");
                }

                Log($"Backup complete → {backupRoot}");
                SetProgress(0, "");
            });

            ToggleButtons(true);
        }

        // ─── ③ Install Mods ─────────────────────────────────────────────
        private async void InstallMods(object? _, EventArgs __)
        {
            if (!CheckGameDir()) return;

            using var dlg = new OpenFileDialog { Title = "Select mod/modpack ZIP", Filter = "ZIP|*.zip" };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            ToggleButtons(false);

            await Task.Run(() =>
            {
                string zipPath = dlg.FileName;
                string tempDir = Path.Combine(Path.GetTempPath(), $"7dtm_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);

                using var z = ZipFile.OpenRead(zipPath);
                long total = z.Entries.Sum(e => e.Length);
                long done  = 0;
                foreach (var entry in z.Entries)
                {
                    string full = Path.Combine(tempDir, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    entry.ExtractToFile(full, true);
                    done += entry.Length;
                    SetProgress((int)(done * 100 / total), $"Extracting {(int)(done * 100 / total)}%");
                }

                var roots = Directory.EnumerateFiles(tempDir, "ModInfo.xml", SearchOption.AllDirectories)
                                     .Select(Path.GetDirectoryName)
                                     .Where(p => p != null)!
                                     .Distinct()
                                     .ToList();
                if (roots.Count == 0)
                {
                    MessageBox.Show("No ModInfo.xml found – invalid archive.", "Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Directory.Delete(tempDir, true);
                    SetProgress(0, "");
                    ToggleButtons(true);
                    return;
                }

                long copyTotal = roots.SelectMany(r => Directory.GetFiles(r!, "*", SearchOption.AllDirectories))
                                      .Sum(f => new FileInfo(f).Length);
                long copied = 0;

                foreach (var r in roots)
                {
                    string dest = Path.Combine(modsDir, Path.GetFileName(r!));
                    if (Directory.Exists(dest)) Directory.Delete(dest, true);
                    CopyDirectoryWithProgress(r!, dest, ref copied, copyTotal, "Installing");
                    Log($"Installed {Path.GetFileName(r!)}");
                }

                var manifest = new Manifest
                {
                    Mods = roots.Select(r => Path.GetFileName(r!)!).Order().ToList()
                };
                File.WriteAllText(Path.Combine(modsDir, MANIFEST_FN),
                                  JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

                Directory.Delete(tempDir, true);
                Log($"Install complete. {manifest.Mods.Count} mod(s) added.");
                SetProgress(0, "");
            });

            ToggleButtons(true);
        }

        // ─── ④ Restore Backup ───────────────────────────────────────────
        private async void RestoreBackup(object? _, EventArgs __)
        {
            if (!CheckGameDir()) return;

            using var dlg = new FolderBrowserDialog { Description = "Pick a ModsBackup_xxxxx folder" };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            string backupMods = Path.Combine(dlg.SelectedPath, MODS_FOLDER);
            if (!Directory.Exists(backupMods))
            {
                MessageBox.Show("Folder doesn’t contain a Mods sub-folder.", "Invalid backup",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ToggleButtons(false);

            await Task.Run(() =>
            {
                foreach (var dir in Directory.EnumerateDirectories(modsDir))
                    if (!Path.GetFileName(dir).Equals(HARMONY_DIR, StringComparison.OrdinalIgnoreCase))
                        Directory.Delete(dir, true);

                long total = Directory.GetFiles(backupMods, "*", SearchOption.AllDirectories)
                                      .Sum(f => new FileInfo(f).Length);
                long copied = 0;

                foreach (var dir in Directory.EnumerateDirectories(backupMods))
                {
                    string dest = Path.Combine(modsDir, Path.GetFileName(dir));
                    CopyDirectoryWithProgress(dir, dest, ref copied, total, "Restoring");
                    Log($"Restored {Path.GetFileName(dir)}");
                }

                Log("Restore complete.");
                SetProgress(0, "");
            });

            ToggleButtons(true);
        }

        // ─── ⑤ Verify Integrity ─────────────────────────────────────────
        private void VerifyIntegrity(object? _, EventArgs __)
        {
            if (!CheckGameDir()) return;

            string manifestPath = Path.Combine(modsDir, MANIFEST_FN);
            if (!File.Exists(manifestPath))
            {
                MessageBox.Show("No manifest found – install mods with this tool first.",
                                "No manifest", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))!;
            var report = manifest.Mods.Select(m =>
                Directory.Exists(Path.Combine(modsDir, m)) ? $"✅ {m}" : $"❌ {m}");
            MessageBox.Show(string.Join(Environment.NewLine, report),
                            "Integrity Check", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ─── copy helper ────────────────────────────────────────────────
        private void CopyDirectoryWithProgress(string src, string dest,
                                               ref long copied, long total,
                                               string phase)
        {
            Directory.CreateDirectory(dest);
            const int BUF = 1024 * 1024;
            byte[] buffer = new byte[BUF];

            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel  = file.Substring(src.Length + 1);
                string dstF = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dstF)!);

                using var inFs  = new FileStream(file, FileMode.Open,  FileAccess.Read);
                using var outFs = new FileStream(dstF,  FileMode.Create, FileAccess.Write);

                int read;
                while ((read = inFs.Read(buffer, 0, BUF)) > 0)
                {
                    outFs.Write(buffer, 0, read);
                    copied += read;
                    int pct = (int)(copied * 100 / Math.Max(1, total));
                    SetProgress(pct, $"{phase} {pct}%");
                }
            }
        }

        // ─── manifest ───────────────────────────────────────────────────
        private class Manifest { public List<string> Mods { get; set; } = new(); }

        // ─── entry point ────────────────────────────────────────────────
        [STAThread]
        public static void Main()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.Run(new InstallerForm());
        }
    }
}
