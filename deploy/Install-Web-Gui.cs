// BODA VMS Web — 오프라인 설치 GUI 런처
//
// 현장 운영자가 PowerShell 명령어 없이 더블클릭으로 설치할 수 있게 하는 얇은 래퍼.
// Install-Web-Offline.ps1 과 publish 폴더를 탐색기로 선택 → [설치 실행] 클릭 →
// 관리자 권한(UAC) PowerShell 로 스크립트를 실행한다.
//
// 빌드: .\Build-InstallGui.ps1  (Windows 내장 csc.exe 사용 — SDK 불필요, C# 5 문법 유지)
// 대상: .NET Framework 4.x (Windows 10/11 기본 내장 — 현장 PC 에 런타임 설치 불필요)

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Boda.Vms.Deploy
{
    public class InstallerForm : Form
    {
        private TextBox _scriptBox;
        private TextBox _sourceBox;
        private Button _runButton;

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }

        public InstallerForm()
        {
            Text = "BODA VMS Web — 오프라인 설치";
            Font = new Font("Segoe UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(640, 240);

            Label scriptLabel = new Label();
            scriptLabel.Text = "설치 스크립트 (Install-Web-Offline.ps1)";
            scriptLabel.Location = new Point(20, 18);
            scriptLabel.AutoSize = true;

            _scriptBox = new TextBox();
            _scriptBox.Location = new Point(20, 40);
            _scriptBox.Size = new Size(500, 25);

            Button scriptBrowse = new Button();
            scriptBrowse.Text = "찾아보기...";
            scriptBrowse.Location = new Point(528, 38);
            scriptBrowse.Size = new Size(92, 27);
            scriptBrowse.Click += OnBrowseScript;

            Label sourceLabel = new Label();
            sourceLabel.Text = "게시 산출물 폴더 (SourcePath — BODA.VMS.Web.exe 가 들어있는 publish 폴더)";
            sourceLabel.Location = new Point(20, 78);
            sourceLabel.AutoSize = true;

            _sourceBox = new TextBox();
            _sourceBox.Location = new Point(20, 100);
            _sourceBox.Size = new Size(500, 25);

            Button sourceBrowse = new Button();
            sourceBrowse.Text = "찾아보기...";
            sourceBrowse.Location = new Point(528, 98);
            sourceBrowse.Size = new Size(92, 27);
            sourceBrowse.Click += OnBrowseSource;

            _runButton = new Button();
            _runButton.Text = "설치 실행";
            _runButton.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _runButton.Location = new Point(20, 145);
            _runButton.Size = new Size(600, 42);
            _runButton.Click += OnRun;

            Label hint = new Label();
            hint.Text = "실행하면 관리자 권한(UAC) 동의 창이 뜹니다. 설치가 끝나도 PowerShell 창을 닫지 말고\r\n" +
                        "화면에 표시되는 결과(초기 admin 비밀번호 등)를 꼭 확인·기록하세요.";
            hint.Location = new Point(20, 196);
            hint.AutoSize = true;
            hint.ForeColor = Color.DimGray;

            Controls.Add(scriptLabel);
            Controls.Add(_scriptBox);
            Controls.Add(scriptBrowse);
            Controls.Add(sourceLabel);
            Controls.Add(_sourceBox);
            Controls.Add(sourceBrowse);
            Controls.Add(_runButton);
            Controls.Add(hint);

            PrefillDefaults();
        }

        // USB 표준 구성(E:\deploy\ 안에 exe+ps1, 옆에 E:\publish\)이면 자동으로 채워줌
        private void PrefillDefaults()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            string script = Path.Combine(exeDir, "Install-Web-Offline.ps1");
            if (File.Exists(script))
                _scriptBox.Text = script;

            string[] candidates = new string[]
            {
                Path.GetFullPath(Path.Combine(exeDir, "..", "publish")),
                Path.Combine(exeDir, "publish")
            };
            foreach (string dir in candidates)
            {
                if (File.Exists(Path.Combine(dir, "BODA.VMS.Web.exe")))
                {
                    _sourceBox.Text = dir;
                    break;
                }
            }
        }

        private void OnBrowseScript(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "설치 스크립트 선택";
                dialog.Filter = "PowerShell 스크립트 (*.ps1)|*.ps1";
                if (_scriptBox.Text.Length > 0 && File.Exists(_scriptBox.Text))
                    dialog.InitialDirectory = Path.GetDirectoryName(_scriptBox.Text);
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _scriptBox.Text = dialog.FileName;
            }
        }

        private void OnBrowseSource(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dialog = new FolderBrowserDialog())
            {
                dialog.Description = "게시 산출물(publish) 폴더 선택 — BODA.VMS.Web.exe 가 들어있어야 합니다.";
                if (_sourceBox.Text.Length > 0 && Directory.Exists(_sourceBox.Text))
                    dialog.SelectedPath = _sourceBox.Text;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _sourceBox.Text = dialog.SelectedPath;
            }
        }

        private void OnRun(object sender, EventArgs e)
        {
            string script = _scriptBox.Text.Trim();
            string source = _sourceBox.Text.Trim();

            if (script.Length == 0 || !File.Exists(script))
            {
                MessageBox.Show(this, "설치 스크립트(.ps1) 파일을 선택하세요.", "확인",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (source.Length == 0 || !Directory.Exists(source))
            {
                MessageBox.Show(this, "게시 산출물 폴더(SourcePath)를 선택하세요.", "확인",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!File.Exists(Path.Combine(source, "BODA.VMS.Web.exe")))
            {
                DialogResult answer = MessageBox.Show(this,
                    "선택한 폴더에 BODA.VMS.Web.exe 가 없습니다.\n" +
                    "publish 산출물 폴더가 맞는지 확인하세요.\n\n그래도 계속할까요?",
                    "산출물 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }

            // -NoExit: 스크립트가 출력하는 결과(자동 생성 admin 비밀번호 등)를 운영자가 볼 수 있게 창 유지
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = string.Format(
                "-NoExit -ExecutionPolicy Bypass -File \"{0}\" -SourcePath \"{1}\"", script, source);
            psi.UseShellExecute = true;
            psi.Verb = "runas";   // 관리자 권한 요청 (UAC)

            try
            {
                Process.Start(psi);
                _runButton.Enabled = false;
                _runButton.Text = "설치 진행 중 — PowerShell 창을 확인하세요";
            }
            catch (Win32Exception)
            {
                // UAC 동의 취소 (ERROR_CANCELLED)
                MessageBox.Show(this,
                    "관리자 권한 동의가 취소되어 설치를 시작하지 못했습니다.\n" +
                    "다시 [설치 실행]을 누르고 UAC 창에서 '예'를 선택하세요.",
                    "권한 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
