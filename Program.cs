// Program.cs – WinForms 7DTD Mod-Installer (stable verbose build)
// Build: dotnet publish -c Release   (SelfContained + PublishSingleFile in .csproj)

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace ModInstaller7DTD
{
    public class InstallerForm : Form
    {
        // ─── constants ────────────────────────────────────────────────────
        const string VANILLA_HINT = @"C:\Steam\steamapps\common\";
        const string EXE_NAME     = "7DaysToDie.exe";
        const string MODS_FOLDER  = "Mods";
        const string HARMONY_DIR  = "0_TFP_Harmony";
        const string MANIFEST_FN  = "manifest7dtm.json";

        // ─── state ────────────────────────────────────────────────────────
        string     gameDir     = string.Empty;   // root dir (…\7 Days To Die)
        string     modsDir     = string.Empty;   // …\Mods
        TextBox    logBox;                       // console-style output

        // ─── constructor ──────────────────────────────────────────────────
        public InstallerForm()
        {
            Text = "7DTD Mod-Installer - by t7";
            ClientSize = new Size(640, 380);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            // Buttons
            var btnPick    = MakeButton("① Pick 7DaysToDie.exe",    10, 10, PickExe);
            var btnBackup  = MakeButton("② Backup Mods",            10, 55, BackupMods);
            var btnInstall = MakeButton("③ Install Mods (ZIP)...",  10, 100, InstallMods);
            var btnRestore = MakeButton("④ Restore Backup…",        10, 145, RestoreBackup);
            var btnVerify  = MakeButton("⑤ Verify Integrity",       10, 190, VerifyIntegrity);

            Controls.AddRange(new Control[] { btnPick, btnBackup, btnInstall, btnRestore, btnVerify });

            // Log textbox
            logBox = new TextBox
            {
                Multiline = true,
                ReadOnly  = true,
                ScrollBars = ScrollBars.Vertical,
                Location  = new Point(250, 10),
                Size      = new Size(370, 330),
                Font      = new Font("Consolas", 9f)
            };
            Controls.Add(logBox);

            Log("Ready. Pick your 7DaysToDie.exe to begin.\r\nHint: " + Path.Combine(VANILLA_HINT, "7 Days To Die"));
        }

        // ─── UI helper ────────────────────────────────────────────────────
        Button MakeButton(string text, int x, int y, EventHandler click)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(220, 35) };
            b.Click += click;
            return b;
        }

        void Log(string msg)
        {
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\r\n");
        }

        // ─── ① Pick game EXE ──────────────────────────────────────────────
        void PickExe(object? s, EventArgs e)
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

            Log($"Game folder set to: {gameDir}");
            if (!Directory.Exists(modsDir)) Directory.CreateDirectory(modsDir);
            // Convenience: open File Explorer for the user.
            Process.Start("explorer.exe", VANILLA_HINT);
        }

        // ─── ② Backup ─────────────────────────────────────────────────────
        void BackupMods(object? s, EventArgs e)
        {
            if (!CheckGameDir()) return;

            var srcFolders = Directory.EnumerateDirectories(modsDir)
                                      .Where(d => !Path.GetFileName(d).Equals(HARMONY_DIR, StringComparison.OrdinalIgnoreCase))
                                      .ToList();
            if (!srcFolders.Any())
            {
                Log("No mods (other than Harmony) found – nothing to back up.");
                return;
            }

            string stamp       = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupRoot  = Path.Combine(gameDir, $"ModsBackup_{stamp}");
            string backupMods  = Path.Combine(backupRoot, MODS_FOLDER);
            Directory.CreateDirectory(backupMods);

            foreach (var folder in srcFolders)
            {
                string dest = Path.Combine(backupMods, Path.GetFileName(folder));
                CopyDirectory(folder, dest);
                Directory.Delete(folder, true); // clean Mods folder after copy
                Log($"Backed up {Path.GetFileName(folder)}");
            }

            Log($"Backup complete → {backupRoot}");
        }

        // ─── ③ Install Mods ───────────────────────────────────────────────
        void InstallMods(object? s, EventArgs e)
        {
            if (!CheckGameDir()) return;

            using var dlg = new OpenFileDialog
            {
                Title  = "Select mod or modpack ZIP",
                Filter = "ZIP archives|*.zip"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var tempExtract = Path.Combine(Path.GetTempPath(), $"7dtm_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempExtract);
            ZipFile.ExtractToDirectory(dlg.FileName, tempExtract);

            // Find every folder that contains a ModInfo.xml
            var modFolders = Directory.EnumerateFiles(tempExtract, "ModInfo.xml", SearchOption.AllDirectories)
                                      .Select(Path.GetDirectoryName)!
                                      .Distinct()
                                      .ToList();

            if (!modFolders.Any())
            {
                MessageBox.Show("No ModInfo.xml files found – not a valid 7DTD mod or pack.", "Invalid archive",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                Directory.Delete(tempExtract, true);
                return;
            }

            foreach (var folder in modFolders)
            {
                string dest = Path.Combine(modsDir, Path.GetFileName(folder));
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                CopyDirectory(folder, dest);
                Log($"Installed {Path.GetFileName(folder)}");
            }

            // Build fresh manifest
            var manifest = new Manifest { Mods = modFolders.Select(Path.GetFileName)!.Order().ToList() };
            File.WriteAllText(Path.Combine(modsDir, MANIFEST_FN),
                              JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            Directory.Delete(tempExtract, true);
            Log($"Install complete. {manifest.Mods.Count} mod(s) added.");
        }

        // ─── ④ Restore Backup ─────────────────────────────────────────────
        void RestoreBackup(object? s, EventArgs e)
        {
            if (!CheckGameDir()) return;

            using var dlg = new FolderBrowserDialog
            {
                Description = "Pick the ModsBackup_xxxxx folder to restore"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            string backupMods = Path.Combine(dlg.SelectedPath, MODS_FOLDER);
            if (!Directory.Exists(backupMods))
            {
                MessageBox.Show("Selected folder doesn’t contain a Mods sub-folder.",
                                "Invalid backup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Clean current Mods (except Harmony)
            foreach (var dir in Directory.EnumerateDirectories(modsDir))
            {
                if (Path.GetFileName(dir).Equals(HARMONY_DIR, StringComparison.OrdinalIgnoreCase)) continue;
                Directory.Delete(dir, true);
            }

            // Copy from backup/Mods
            foreach (var dir in Directory.EnumerateDirectories(backupMods))
            {
                string dest = Path.Combine(modsDir, Path.GetFileName(dir));
                CopyDirectory(dir, dest);
                Log($"Restored {Path.GetFileName(dir)}");
            }

            Log("Restore complete.");
        }

        // ─── ⑤ Verify Integrity ──────────────────────────────────────────
        void VerifyIntegrity(object? s, EventArgs e)
        {
            if (!CheckGameDir()) return;

            string manifestPath = Path.Combine(modsDir, MANIFEST_FN);
            if (!File.Exists(manifestPath))
            {
                Log("No manifest found – install mods with this tool first.");
                return;
            }

            Manifest manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath))!;
            var missing = new List<string>();
            foreach (string mod in manifest.Mods)
                if (!Directory.Exists(Path.Combine(modsDir, mod))) missing.Add(mod);

            if (missing.Count == 0)
                Log("Integrity OK – all mods present.");
            else
                Log($"Integrity FAILED – missing: {string.Join(", ", missing)}");
        }

        // ─── helpers ──────────────────────────────────────────────────────
        bool CheckGameDir()
        {
            if (string.IsNullOrEmpty(gameDir))
            {
                MessageBox.Show("Pick your 7DaysToDie.exe first.", "Game folder not set",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        static void CopyDirectory(string src, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel  = file.Substring(src.Length + 1);
                string destFile = Path.Combine(dest, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                File.Copy(file, destFile, true);
            }
        }

        // ─── manifest DTO ────────────────────────────────────────────────
        class Manifest
        {
            public List<string> Mods { get; set; } = new();
        }

        // ─── entry point ─────────────────────────────────────────────────
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.Run(new InstallerForm());
        }
    }
}
