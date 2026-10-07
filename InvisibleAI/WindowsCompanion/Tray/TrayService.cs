using System.Drawing;
using Forms = System.Windows.Forms;
using InvisibleAI.Companion.Core;

namespace InvisibleAI.Companion.Tray;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem state;
    public TrayService(AssistantController controller)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Invisible AI Assistant") { Enabled = false });
        state = new Forms.ToolStripMenuItem("● Enabled", null, (_, _) => controller.Toggle());
        menu.Items.Add(state); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Send Clipboard to AI", null, (_, _) => controller.RunSafely(controller.SendClipboardAsync));
        menu.Items.Add("Select Screen", null, (_, _) => controller.RunSafely(controller.SelectScreenAsync));
        menu.Items.Add("Expand Last Response", null, (_, _) => controller.ExpandLast());
        menu.Items.Add("Settings", null, (_, _) => controller.ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Enable/Disable", null, (_, _) => controller.Toggle());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        icon = new Forms.NotifyIcon { Text = "Invisible AI Assistant", Icon = SystemIcons.Information, ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => controller.ShowSettings();
        controller.SettingsChanged += () => { state.Text = controller.Settings.Enabled ? "● Enabled" : "○ Disabled"; icon.Visible = controller.Settings.TrayMode; };
        state.Text = controller.Settings.Enabled ? "● Enabled" : "○ Disabled";
        icon.Visible = controller.Settings.TrayMode;
    }
    public void Notify(string text) { icon.BalloonTipTitle = "Invisible AI Assistant"; icon.BalloonTipText = text; icon.ShowBalloonTip(5000); }
    public void Dispose() { icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose(); }
}
