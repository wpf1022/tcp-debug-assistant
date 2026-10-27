using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Windows.Forms;

namespace TcpDebugAssistant
{
    /// <summary>
    /// 设计系统（来源：ui-ux-pro-max · Luxury/Premium 黑金轻奢 · 调色板 #51 Premium Analogous 051）
    /// 香槟金主操作 + 暖黑背景，正文/背景对比度 &gt; 14:1（WCAG AA 要求 4.5:1）。
    /// </summary>
    internal static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(0x1C, 0x18, 0x12); // #1C1812 暖黑页面背景
        public static readonly Color Surface   = Color.FromArgb(0x24, 0x1F, 0x16); // 面板
        public static readonly Color Surface2  = Color.FromArgb(0x2E, 0x27, 0x1A); // 控件/选中
        public static readonly Color Border    = Color.FromArgb(0x45, 0x3A, 0x24); // 暗金边框
        public static readonly Color Gold      = Color.FromArgb(0xC3, 0xA5, 0x79); // #C3A579 香槟金（主色）
        public static readonly Color GoldHi    = Color.FromArgb(0xD6, 0xBB, 0x92); // 金·悬停
        public static readonly Color GoldDim   = Color.FromArgb(0x8A, 0x76, 0x54); // 金·弱化（下划线/分隔）
        public static readonly Color Text      = Color.FromArgb(0xF2, 0xEE, 0xE4); // 暖白主文本
        public static readonly Color Muted     = Color.FromArgb(0xA8, 0x9F, 0x8D); // 暖灰次级文本
        public static readonly Color OnAccent  = Color.FromArgb(0x1C, 0x18, 0x12); // 金底上的深字
        public static readonly Color Danger    = Color.FromArgb(0xA8, 0x5C, 0x50); // 砖红（断开/踢出）
        public static readonly Color DangerHi  = Color.FromArgb(0xBC, 0x6E, 0x62);
        public static readonly Color Ok        = Color.FromArgb(0xA8, 0xC6, 0x9A); // 鼠尾草绿·就绪
        public static readonly Color LogBg     = Color.FromArgb(0x14, 0x11, 0x0C); // 日志终端底
        public static readonly Color Sent      = Color.FromArgb(0xD6, 0xBB, 0x92); // 发送流·香槟金
        public static readonly Color Recv      = Color.FromArgb(0xA8, 0xC6, 0x9A); // 接收流·鼠尾草
        public static readonly Color Info      = Color.FromArgb(0x8D, 0x86, 0x7A); // 状态流·暖灰

        public static Button MkBtn(string text, int width, Color fill, Color hover)
        {
            var b = new Button
            {
                Text = text, Width = width, Height = 30,
                FlatStyle = FlatStyle.Flat, BackColor = fill,
                ForeColor = fill == Danger ? Color.White : OnAccent,   // 金底深字更轻奢
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = hover;
            b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(hover);
            return b;
        }
    }

    public class MainForm : Form
    {
        // ---------- 公共 UI ----------
        private TabControl _tabs;
        private StatusStrip _status;
        private ToolStripStatusLabel _statusLabel;

        // ---------- 客户端页 ----------
        private TcpClientController _client = new();
        private TextBox _cliHost, _cliPort;
        private Button _cliConnect;
        private Label _cliState;
        private PageWidgets _cliPage;

        // ---------- 服务端页 ----------
        private TcpServerController _server = new();
        private ComboBox _srvHost;
        private TextBox _srvPort;
        private Button _srvListen;
        private Label _srvState;
        private ListView _clientList;
        private CheckBox _srvSendAll;
        private PageWidgets _srvPage;

        private readonly Encoding[] _encodings = { Encoding.UTF8, Encoding.ASCII };

        public MainForm()
        {
            BuildUi();
            WireClient();
            WireServer();
        }

        // ================================================================
        //  UI 构建
        // ================================================================
        private void BuildUi()
        {
            Text = "TCP 调试助手 · TcpDebugAssistant v1.4 轻奢版";
            Size = new Size(1080, 720);
            MinimumSize = new Size(860, 560);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Theme.Bg;
            DoubleBuffered = true;

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                Padding = new Point(28, 12),
                ItemSize = new Size(150, 42),
                BackColor = Theme.Bg
            };
            _tabs.TabPages.Add(MakeClientTab());
            _tabs.TabPages.Add(MakeServerTab());
            _tabs.DrawItem += DrawTabItem;
            _tabs.Paint += PaintTabBand;                // 填掉标签栏右侧/左侧的系统白底
            _tabs.SelectedIndexChanged += (s, e) => _tabs.Invalidate();

            _status = new StatusStrip
            {
                BackColor = Theme.Surface,
                SizingGrip = true,
                Renderer = new ToolStripProfessionalRenderer(new DarkColorTable())
            };
            _statusLabel = new ToolStripStatusLabel("就绪")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Muted
            };
            _status.Items.Add(_statusLabel);

            Controls.Add(_tabs);
            Controls.Add(_status);
            // 轻奢点缀：窗口顶端一条香槟金细线（后添加者先 Dock，位于最顶部）
            var goldTrim = new Panel { Dock = DockStyle.Top, Height = 3, BackColor = Theme.GoldDim };
            Controls.Add(goldTrim);
        }

        /// <summary>把标签栏中不属于任何页签的空白填成背景色（消除系统白底）。</summary>
        private void PaintTabBand(object s, PaintEventArgs e)
        {
            var band = new Rectangle(0, 0, _tabs.ClientSize.Width, _tabs.ItemSize.Height + 6);
            using (var rgn = new Region(band))
            {
                for (int i = 0; i < _tabs.TabPages.Count; i++) rgn.Exclude(_tabs.GetTabRect(i));
                using (var b = new SolidBrush(Theme.Bg)) e.Graphics.FillRegion(b, rgn);
            }
        }

        /// <summary>列宽自适应：撑满列表宽度，避免表头右侧留白。</summary>
        private void FitColumns()
        {
            if (_clientList == null || _clientList.Columns.Count < 5) return;
            int total = _clientList.ClientSize.Width - 4;
            if (total < 320) return;
            int fixedSum = 150 + 90 + 90 + 80;
            _clientList.Columns[0].Width = Math.Max(160, total - fixedSum);
            _clientList.Columns[1].Width = 150;
            _clientList.Columns[2].Width = 90;
            _clientList.Columns[3].Width = 90;
            _clientList.Columns[4].Width = 80;
        }

        /// <summary>轻奢风标签头：选中态香槟金文字 + 金色下划线。</summary>
        private void DrawTabItem(object s, DrawItemEventArgs e)
        {
            var g = e.Graphics;
            bool sel = _tabs.SelectedIndex == e.Index;
            using (var bg = new SolidBrush(sel ? Theme.Surface2 : Theme.Bg))
                g.FillRectangle(bg, e.Bounds);
            var font = new Font("Microsoft YaHei UI", 10f, sel ? FontStyle.Bold : FontStyle.Regular);
            TextRenderer.DrawText(g, _tabs.TabPages[e.Index].Text, font, e.Bounds,
                sel ? Theme.Gold : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (sel)
                using (var bar = new SolidBrush(Theme.Gold))
                    g.FillRectangle(bar, e.Bounds.X + 24, e.Bounds.Bottom - 3, e.Bounds.Width - 48, 3);
        }

        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color StatusStripGradientBegin => Theme.Surface;
            public override Color StatusStripGradientEnd => Theme.Surface;
            public override Color ToolStripGradientBegin => Theme.Surface;
            public override Color ToolStripGradientMiddle => Theme.Surface;
            public override Color ToolStripGradientEnd => Theme.Surface;
            public override Color ToolStripBorder => Theme.Surface;
            public override Color SeparatorDark => Theme.Border;
            public override Color SeparatorLight => Theme.Border;
            public override Color ImageMarginGradientBegin => Theme.Surface;
            public override Color ImageMarginGradientMiddle => Theme.Surface;
            public override Color ImageMarginGradientEnd => Theme.Surface;
        }

        private TabPage MakeClientTab()
        {
            var page = new TabPage("客户端模式") { BackColor = Theme.Surface };

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 48, WrapContents = false,
                BackColor = Theme.Surface, Padding = new Padding(10, 11, 10, 0)
            };
            var lblAddr = new Label { Text = "服务器地址", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 5, 8, 0) };
            _cliHost = MkInput("127.0.0.1", 150); _cliHost.Margin = new Padding(0, 0, 8, 0);
            _cliPort = MkInput("9000", 70); _cliPort.Margin = new Padding(0, 0, 8, 0);
            _cliConnect = Theme.MkBtn("连接", 90, Theme.Gold, Theme.GoldHi);
            _cliConnect.Margin = new Padding(0, 0, 14, 0);
            _cliState = new Label { Text = "● 未连接", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 5, 0, 0) };
            top.Controls.AddRange(new Control[] { lblAddr, _cliHost, _cliPort, _cliConnect, _cliState });

            _cliPage = MakePage();
            page.Controls.Add(_cliPage.Root);
            page.Controls.Add(top);
            return page;
        }

        private TabPage MakeServerTab()
        {
            var page = new TabPage("服务端模式") { BackColor = Theme.Surface };

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top, Height = 48, WrapContents = false,
                BackColor = Theme.Surface, Padding = new Padding(10, 11, 10, 0)
            };
            var lblHost = new Label { Text = "监听地址", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 5, 8, 0) };
            _srvHost = new ComboBox
            {
                Width = 140, DropDownStyle = ComboBoxStyle.DropDown, FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Surface2, ForeColor = Theme.Text, Margin = new Padding(0, 0, 10, 0)
            };
            _srvHost.Items.Add("0.0.0.0");                      // 全部网卡（默认）
            foreach (var ip in LocalIPv4()) _srvHost.Items.Add(ip);
            _srvHost.Text = "0.0.0.0";
            var lblPort = new Label { Text = "端口", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 5, 8, 0) };
            _srvPort = MkInput("9000", 70); _srvPort.Margin = new Padding(0, 0, 8, 0);
            _srvListen = Theme.MkBtn("开始监听", 100, Theme.Gold, Theme.GoldHi);
            _srvListen.Margin = new Padding(0, 0, 14, 0);
            _srvState = new Label { Text = "● 未监听", AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(0, 5, 16, 0) };
            _srvSendAll = new CheckBox
            {
                Text = "发送到所有客户端", AutoSize = true,
                ForeColor = Theme.Muted, Checked = true,     // 默认广播：避免「选了没反应」
                Margin = new Padding(0, 5, 0, 0)
            };
            top.Controls.AddRange(new Control[] { lblHost, _srvHost, lblPort, _srvPort, _srvListen, _srvState, _srvSendAll });

            // 客户端列表
            _clientList = new ListView
            {
                Dock = DockStyle.Top, Height = 140, View = View.Details,
                FullRowSelect = true, HideSelection = false, OwnerDraw = true,
                BackColor = Theme.LogBg, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle
            };
            _clientList.Columns.Add("客户端", 200);
            _clientList.Columns.Add("连接时间", 150);
            _clientList.Columns.Add("接收字节", 90);
            _clientList.Columns.Add("发送字节", 90);
            _clientList.Columns.Add("状态", 80);
            _clientList.DrawColumnHeader += DrawListHeader;
            _clientList.DrawSubItem += (s, e) => e.DrawDefault = true;
            _clientList.Resize += (s, e) => FitColumns();
            _clientList.HandleCreated += (s, e) => FitColumns();

            var kick = Theme.MkBtn("断开选中客户端", 130, Theme.Danger, Theme.DangerHi);
            kick.Left = 8;
            _clientList.Controls.Add(kick);
            kick.Location = new Point(8, _clientList.Height - 38);

            _srvPage = MakePage();
            page.Controls.Add(_srvPage.Root);
            page.Controls.Add(_clientList);
            page.Controls.Add(top);
            return page;
        }

        private void DrawListHeader(object s, DrawListViewColumnHeaderEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(Theme.Surface2))
                g.FillRectangle(b, e.Bounds);
            g.DrawLine(new Pen(Theme.Border), e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
            g.DrawLine(new Pen(Theme.Border), e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            TextRenderer.DrawText(g, _clientList.Columns[e.ColumnIndex].Text, Font, e.Bounds,
                Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
        }

        private static TextBox MkInput(string text, int width) => new()
        {
            Text = text, Width = width, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Surface2, ForeColor = Theme.Text
        };

        /// <summary>一页共用的：日志区 + 发送区。</summary>
        private PageWidgets MakePage()
        {
            var w = new PageWidgets();
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
                BackColor = Theme.Bg
            };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));

            // ---- 日志区 ----
            var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Theme.Surface };
            w.Log = new RichTextBox
            {
                Dock = DockStyle.Fill, ReadOnly = true, BackColor = Theme.LogBg,
                ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9.5f), DetectUrls = false
            };
            var logBar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Theme.Surface };
            var opt = (Action)(() => RebuildLog(w));
            w.ChkTimestamp = MkCheck("时间戳", 8, true, opt);
            w.ChkHexView = MkCheck("HEX 显示", 100, false, opt);
            var btnClear = Theme.MkBtn("清空日志", 80, Theme.Surface2, Theme.Border);
            btnClear.Left = 200; btnClear.Top = 2; btnClear.ForeColor = Theme.Text;
            btnClear.Click += (s, e) => { w.Entries.Clear(); w.Log.Clear(); };
            logBar.Controls.AddRange(new Control[] { w.ChkTimestamp, w.ChkHexView, btnClear });

            logPanel.Controls.Add(w.Log);
            logPanel.Controls.Add(logBar);
            root.Controls.Add(logPanel, 0, 0);

            // ---- 发送区 ----
            var sendPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 4, 8, 8), BackColor = Theme.Surface };
            w.SendRow = new Panel { Dock = DockStyle.Top, Height = 36, BackColor = Theme.Surface };
            w.RbAscii = MkRadio("ASCII/UTF-8", 8, true);
            w.RbHex = MkRadio("HEX", 120, false);
            w.CmbEnc = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, Width = 80, Left = 190, Top = 6,
                FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface2, ForeColor = Theme.Text
            };
            w.CmbEnc.Items.Add("UTF-8");
            w.CmbEnc.Items.Add("ASCII");
            w.CmbEnc.SelectedIndex = 0;
            w.ChkAuto = MkCheck("定时发送", 290, false, null);
            w.NumInterval = new NumericUpDown
            {
                Left = 380, Top = 6, Width = 70, Minimum = 50, Maximum = 60000, Value = 1000,
                BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
            };
            var lblMs = AddLabel(w.SendRow, "ms", 455, 11, Theme.Muted);
            w.NumTimes = new NumericUpDown
            {
                Left = 500, Top = 6, Width = 60, Minimum = 0, Maximum = 9999, Value = 0,
                BackColor = Theme.Surface2, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle
            };
            AddLabel(w.SendRow, "次(0=∞)", 565, 11, Theme.Muted);
            w.BtnSend = Theme.MkBtn("发送", 90, Theme.Gold, Theme.GoldHi);
            w.BtnSend.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            w.SendRow.Controls.AddRange(new Control[] { w.RbAscii, w.RbHex, w.CmbEnc, w.ChkAuto, w.NumInterval, lblMs, w.NumTimes, w.BtnSend });
            w.SendRow.Resize += (s, e) => { w.BtnSend.Left = w.SendRow.Width - 100; };

            w.Stats = new Label
            {
                Dock = DockStyle.Bottom, Height = 24, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Theme.Muted, Text = "发送 0 B   |   接收 0 B"
            };
            w.Input = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true,
                BackColor = Theme.LogBg, ForeColor = Theme.Text,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10f)
            };
            sendPanel.Controls.Add(w.Input);
            sendPanel.Controls.Add(w.SendRow);
            sendPanel.Controls.Add(w.Stats);
            root.Controls.Add(sendPanel, 0, 1);

            w.Root = root;
            return w;
        }

        private static CheckBox MkCheck(string text, int left, bool @checked, Action onChanged)
        {
            var c = new CheckBox { Text = text, Left = left, Top = 6, AutoSize = true, Checked = @checked, ForeColor = Theme.Muted };
            if (onChanged != null) c.CheckedChanged += (s, e) => onChanged();
            return c;
        }
        private static RadioButton MkRadio(string text, int left, bool @checked)
        {
            var c = new RadioButton { Text = text, Left = left, Top = 6, AutoSize = true, Checked = @checked, ForeColor = Theme.Muted };
            return c;
        }
        private static Label AddLabel(Control parent, string text, int left, int top, Color color)
        {
            var l = new Label { Text = text, Left = left, Top = top, AutoSize = true, ForeColor = color };
            parent.Controls.Add(l);
            return l;
        }

        // ================================================================
        //  事件接线
        // ================================================================
        private void WireClient()
        {
            _cliConnect.Click += async (s, e) =>
            {
                if (!_client.IsConnected)
                {
                    if (!int.TryParse(_cliPort.Text.Trim(), out var port) || port < 1 || port > 65535)
                    { SetStatus("端口无效（1-65535）", true); return; }
                    SetUiBusy(_cliConnect, "断开");
                    await _client.ConnectAsync(_cliHost.Text.Trim(), port);
                }
                else
                {
                    await _client.CloseAsync();
                }
            };
            _client.Traffic += e => BeginInvoke(() => OnTraffic(_cliPage, e));
            _client.State += e => BeginInvoke(() =>
            {
                OnNetState(_cliPage, e);
                _cliState.Text = _client.IsConnected ? "● 已连接 " + _client.Remote : "● 未连接";
                _cliState.ForeColor = _client.IsConnected ? Theme.Ok : Theme.Muted;
                _cliConnect.Text = _client.IsConnected ? "断开" : "连接";
                _cliConnect.BackColor = _client.IsConnected ? Theme.Danger : Theme.Gold;
                if (_client.IsConnected) SetUiBusy(_cliConnect, "断开");
            });
            SetupSendArea(_cliPage, async bytes =>
            {
                if (!_client.IsConnected) { SetStatus("客户端未连接，无法发送", true); return; }
                await _client.SendAsync(bytes);
                SetStatus($"已发送 {bytes.Length} 字节 → {_client.Remote}", false);
            });
        }

        private void WireServer()
        {
            _srvListen.Click += async (s, e) =>
            {
                if (!_server.IsListening)
                {
                    var hostText = _srvHost.Text.Trim();
                    if (hostText.Contains('(')) hostText = hostText.Substring(0, hostText.IndexOf('(')).Trim();  // 容忍 "0.0.0.0 (全部网卡)"
                    if (hostText.Length == 0) hostText = "0.0.0.0";

                    if (!IPAddress.TryParse(hostText, out var bindAddr))
                    { SetStatus($"监听地址「{hostText}」不是合法 IP（示例：0.0.0.0 / 192.168.1.10）", true); _srvHost.Focus(); return; }
                    if (!int.TryParse(_srvPort.Text.Trim(), out var port) || port < 1 || port > 65535)
                    { SetStatus("端口无效（1-65535）", true); return; }

                    bool isLocal = bindAddr.Equals(IPAddress.Any) || IPAddress.IsLoopback(bindAddr)
                                   || LocalIPv4().Contains(bindAddr.ToString());
                    if (!isLocal)
                    { SetStatus($"{hostText} 不是本机网卡地址，绑定会失败（可用地址：{string.Join(" / ", LocalIPv4())}）", true); }

                    SetUiBusy(_srvListen, "停止监听");
                    bool ok = await _server.StartAsync(bindAddr, port);
                    if (!ok) SetUiBusy(_srvListen, "开始监听");
                }
                else
                {
                    await _server.StopAsync();
                }
                RefreshServerUi();
            };
            _server.Traffic += e => BeginInvoke(() => OnTraffic(_srvPage, e));
            _server.State += e => BeginInvoke(() =>
            {
                OnNetState(_srvPage, e);
                RefreshServerUi();
            });
            _server.ClientConnected += cs => BeginInvoke(() => { AddClientRow(cs); RefreshServerUi(); });
            _server.ClientDisconnected += cs => BeginInvoke(() => { RemoveClientRow(cs); RefreshServerUi(); });
            SetupSendArea(_srvPage, async bytes =>
            {
                if (!_server.IsListening) { SetStatus("服务端未监听，无法发送", true); return; }
                if (_server.Sessions.Count == 0) { SetStatus("还没有客户端接入", true); return; }
                if (_srvSendAll.Checked)
                {
                    await _server.SendToAllAsync(bytes);
                    SetStatus($"已广播 {bytes.Length} 字节 → {_server.Sessions.Count} 个客户端", false);
                }
                else
                {
                    if (_clientList.SelectedItems.Count == 0)
                    {   // 未选中时自动回退到第一个客户端，避免「点了没反应」
                        _clientList.Items[0].Selected = true;
                    }
                    if (_clientList.SelectedItems[0].Tag is ClientSession cs)
                    {
                        await _server.SendAsync(cs, bytes);
                        SetStatus($"已发送 {bytes.Length} 字节 → {cs.Tag}", false);
                    }
                }
            });
        }

        private void SetupSendArea(PageWidgets w, Func<byte[], Task> sender)
        {
            w.Timer = new System.Windows.Forms.Timer { Interval = (int)w.NumInterval.Value };
            int fired = 0;
            w.Timer.Tick += async (s, e) =>
            {
                var bytes = BuildPayload(w);
                if (bytes == null) return;
                await sender(bytes);
                fired++;
                if (w.NumTimes.Value > 0 && fired >= w.NumTimes.Value)
                {
                    w.Timer.Stop(); w.ChkAuto.Checked = false; fired = 0;
                }
            };
            w.ChkAuto.CheckedChanged += (s, e) =>
            {
                if (w.ChkAuto.Checked)
                {
                    w.Timer.Interval = (int)w.NumInterval.Value;
                    fired = 0;
                    w.Timer.Start();
                    SetStatus($"定时发送已启动：每 {w.NumInterval.Value} ms", false);
                }
                else w.Timer.Stop();
            };
            w.NumInterval.ValueChanged += (s, e) => { if (w.Timer.Enabled) w.Timer.Interval = (int)w.NumInterval.Value; };
            w.BtnSend.Click += async (s, e) =>
            {
                if (w.Input.TextLength == 0) { SetStatus("请先在下方输入要发送的内容", true); w.Input.Focus(); return; }
                var bytes = BuildPayload(w);
                if (bytes != null) await sender(bytes);
            };
        }

        private byte[] BuildPayload(PageWidgets w)
        {
            var text = w.Input.Text;
            if (text.Length == 0) return null;
            if (w.RbHex.Checked)
            {
                var bytes = Codec.FromHex(text);
                if (bytes == null) { SetStatus("HEX 格式错误（示例：01 A0 FF）", true); return null; }
                return bytes;
            }
            return Codec.Encode(text, _encodings[w.CmbEnc.SelectedIndex]);
        }

        // ================================================================
        //  日志
        // ================================================================
        private void OnTraffic(PageWidgets w, TrafficEventArgs e)
        {
            w.Entries.Add(new LogEntry { Time = e.Time, Sent = e.Sent, Tag = e.Tag, Data = e.Data });
            if (w.Entries.Count > 3000) w.Entries.RemoveRange(0, 500);
            if (e.Sent) w.TxBytes += e.Data.Length; else w.RxBytes += e.Data.Length;
            UpdateStats(w);
            AppendEntry(w, w.Entries[w.Entries.Count - 1]);
        }

        private void OnNetState(PageWidgets w, StateEventArgs e)
        {
            SetStatus(e.Message, e.IsError);
            var color = e.IsError ? Theme.Danger : Theme.Info;
            w.Entries.Add(new LogEntry { Time = e.Time, Sent = true, IsInfo = true, Text = e.Message });
            AppendEntry(w, w.Entries[w.Entries.Count - 1], color);
        }

        private void AppendEntry(PageWidgets w, LogEntry en, Color? forceColor = null)
        {
            if (w.Log.TextLength > 1_500_000) { w.Log.Clear(); }
            var sb = new StringBuilder();
            if (w.ChkTimestamp.Checked) sb.Append(en.Time.ToString("[HH:mm:ss.fff] "));
            if (en.IsInfo) { sb.AppendLine(en.Text); }
            else
            {
                sb.Append(en.Sent ? "→ " : "← ").Append('[').Append(en.Tag).Append("] ");
                AppendBody(sb, en.Data, w.ChkHexView.Checked);
                sb.AppendLine();
            }
            w.Log.SelectionStart = w.Log.TextLength;
            w.Log.SelectionColor = forceColor ?? (en.IsInfo ? Theme.Info : en.Sent ? Theme.Sent : Theme.Recv);
            w.Log.AppendText(sb.ToString());
            w.Log.SelectionStart = w.Log.TextLength;
            w.Log.ScrollToCaret();
        }

        private static void AppendBody(StringBuilder sb, byte[] data, bool hex)
        {
            if (hex)
            {
                sb.Append(Codec.ToHex(data, data.Length));
                if (data.Length <= 512)
                {
                    var s = Encoding.UTF8.GetString(data);
                    var printable = new string(s.Select(c => char.IsControl(c) ? '.' : c).ToArray());
                    sb.Append("   |  \"").Append(printable).Append('"');
                }
            }
            else
            {
                var s = Encoding.UTF8.GetString(data);
                sb.Append(new string(s.Select(c => c == '\r' ? '\0' : c).ToArray()).Replace("\0", ""));
            }
        }

        private void RebuildLog(PageWidgets w)
        {
            w.Log.Clear();
            foreach (var en in w.Entries) AppendEntry(w, en);
        }

        private void UpdateStats(PageWidgets w)
        {
            w.Stats.Text = $"发送 {Fmt(w.TxBytes)}   |   接收 {Fmt(w.RxBytes)}";
        }
        private static string Fmt(long b) => b >= 1048576 ? (b / 1048576.0).ToString("F2") + " MB"
                                            : b >= 1024 ? (b / 1024.0).ToString("F1") + " KB" : b + " B";

        // ================================================================
        //  服务端列表 & 状态
        // ================================================================
        private void AddClientRow(ClientSession cs)
        {
            var it = new ListViewItem(cs.Tag) { Tag = cs };
            it.SubItems.Add(cs.ConnectTime.ToString("HH:mm:ss"));
            it.SubItems.Add("0");
            it.SubItems.Add("0");
            it.SubItems.Add("在线");
            _clientList.Items.Add(it);
            if (_clientList.SelectedItems.Count == 0) it.Selected = true;   // 首个接入者自动选中
        }
        private void RemoveClientRow(ClientSession cs)
        {
            foreach (ListViewItem it in _clientList.Items)
                if (ReferenceEquals(it.Tag, cs)) { _clientList.Items.Remove(it); return; }
        }
        private void RefreshServerUi()
        {
            foreach (ListViewItem it in _clientList.Items)
            {
                if (it.Tag is ClientSession cs)
                {
                    it.SubItems[2].Text = cs.RecvBytes.ToString();
                    it.SubItems[3].Text = cs.SentBytes.ToString();
                }
            }
            _srvState.Text = _server.IsListening
                ? $"● 监听中 · {_server.ListeningEndpoint} · {_server.Sessions.Count} 个客户端"
                : "● 未监听";
            _srvState.ForeColor = _server.IsListening ? Theme.Ok : Theme.Muted;
            _srvListen.Text = _server.IsListening ? "停止监听" : "开始监听";
            _srvListen.BackColor = _server.IsListening ? Theme.Danger : Theme.Gold;
            _srvListen.ForeColor = _server.IsListening ? Color.White : Theme.OnAccent;
            _srvHost.Enabled = !_server.IsListening;    // 监听中锁定地址，避免误解
            _srvPort.ReadOnly = _server.IsListening;
        }

        /// <summary>枚举本机 IPv4 地址（用于监听地址候选）。</summary>
        internal static List<string> LocalIPv4()
        {
            var list = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        var ip = ua.Address.ToString();
                        if (!list.Contains(ip)) list.Add(ip);
                    }
                }
            }
            catch { }
            return list;
        }

        private void SetStatus(string msg, bool err)
        {
            _statusLabel.Text = $"{DateTime.Now:HH:mm:ss}  {msg}";
            _statusLabel.ForeColor = err ? Theme.DangerHi : Theme.Muted;
        }

        private static void SetUiBusy(Button b, string text) { b.Text = text; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _client.Dispose();
            _server.Dispose();
            base.OnFormClosing(e);
        }

        // ================================================================
        private class LogEntry
        {
            public DateTime Time;
            public bool Sent;
            public bool IsInfo;
            public string Text;
            public string Tag;
            public byte[] Data;
        }

        private class PageWidgets
        {
            public TableLayoutPanel Root;
            public RichTextBox Log;
            public TextBox Input;
            public Panel SendRow;
            public RadioButton RbAscii, RbHex;
            public ComboBox CmbEnc;
            public CheckBox ChkHexView, ChkTimestamp, ChkAuto;
            public NumericUpDown NumInterval, NumTimes;
            public Button BtnSend;
            public Label Stats;
            public System.Windows.Forms.Timer Timer;
            public readonly List<LogEntry> Entries = new();
            public long TxBytes, RxBytes;
        }
    }
}
