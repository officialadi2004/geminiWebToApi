using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace InvisibleAI.Companion.Settings;

public sealed class SettingsWindow : Window
{
    private readonly SettingsViewModel viewModel;
    private readonly PasswordBox password = new();
    public SettingsWindow(SettingsViewModel vm)
    {
        viewModel = vm; DataContext = vm; Title = "Invisible AI Assistant · Settings";
        Width = 740; Height = 680; MinWidth = 600; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 13; Background = new SolidColorBrush(Color.FromRgb(246, 248, 250));
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new TextBlock { Text = "Invisible AI Assistant", FontSize = 25, FontWeight = FontWeights.SemiBold });
        header.Children.Add(new TextBlock { Text = "Your screen comes first. Explicit actions only.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 5, 0, 0) });
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkSlateGray, MinHeight = 38 };
        status.SetBinding(TextBlock.TextProperty, new Binding("Status")); footer.Children.Add(status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var reset = new Button { Content = "Restore defaults", Padding = new Thickness(14, 7, 14, 7), Command = vm.ResetCommand };
        var close = new Button { Content = "Close", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7) };
        close.Click += (_, _) => Close();
        var save = new Button { Content = "Save settings", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 7, 14, 7), IsDefault = true };
        save.Click += (_, _) =>
        {
            System.Windows.Input.Keyboard.ClearFocus();
            if (HasValidationError(root)) { vm.Status = "Correct the highlighted fields before saving."; return; }
            vm.SaveCommand.Execute(null);
        };
        buttons.Children.Add(reset); buttons.Children.Add(close); buttons.Children.Add(save); footer.Children.Add(buttons);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var tabs = new TabControl(); root.Children.Add(tabs);
        var general = Tab(tabs, "General");
        Check(general, "Enable assistant", "Enabled"); Check(general, "Start with Windows", "StartWithWindows");
        Check(general, "Start minimized", "StartMinimized"); Check(general, "System tray mode", "TrayMode");
        Note(general, "When idle, no overlay exists on screen. Turning off tray mode keeps Settings available as a normal window.");
        var appearance = Tab(tabs, "Appearance");
        Field(appearance, "Dot size (DIP, 3–16)", "DotSize"); Field(appearance, "Dot opacity (0.1–1)", "DotOpacity");
        Field(appearance, "Pulse cycle (ms, 300–3000)", "PulseMilliseconds"); Field(appearance, "Response opacity (0.2–1)", "ResponseOpacity");
        Field(appearance, "Response font size (9–24)", "ResponseFontSize"); Field(appearance, "Response width (DIP, 100–600)", "ResponseWidth");
        Field(appearance, "Response duration (seconds, 2–120)", "ResponseSeconds"); Field(appearance, "Right offset (DIP)", "OffsetX"); Field(appearance, "Bottom offset (DIP)", "OffsetY");
        Choice(appearance, "Monitor", "Monitor", new[] { "Active", "Primary" }.Concat(Forms.Screen.AllScreens.Select(s => s.DeviceName)).ToArray());
        Check(appearance, "Make response interactive (click to expand)", "InteractiveResponse");
        var ai = Tab(tabs, "AI");
        Note(ai, "Choose how AI responses are generated. Only your selected provider is used. There is no automatic fallback.");
        var providerRow = Row(ai, "AI Provider");
        var provider = new ComboBox { ItemsSource = new[] { AI.Providers.Gemini, AI.Providers.Groq }, Padding = new Thickness(5) };
        provider.SetBinding(ComboBox.SelectedItemProperty, new Binding("Provider") { Mode = BindingMode.TwoWay });
        Grid.SetColumn(provider, 1); providerRow.Children.Add(provider);
        var warning = new TextBlock { Text = "Gemini cookies are sensitive authentication credentials. Paste cookies only from your own Gemini account. Use the Cookie request header containing __Secure-1PSID and optionally __Secure-1PSIDTS.", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkGoldenrod, Margin = new Thickness(0, 0, 0, 14) };
        warning.SetBinding(VisibilityProperty, new Binding("IsGemini") { Converter = new BooleanToVisibilityConverter() }); ai.Children.Add(warning);
        var keyRow = Row(ai, ""); ((TextBlock)keyRow.Children[0]).SetBinding(TextBlock.TextProperty, new Binding("CredentialLabel")); password.Padding = new Thickness(5); password.MaxLength = 65536;
        Grid.SetColumn(password, 1); keyRow.Children.Add(password); password.PasswordChanged += (_, _) => vm.PendingCredential = password.Password;
        vm.Saved += () => password.Clear();
        Note(ai, "Blank keeps the saved credential. Windows encrypts saved credentials; their values are never returned to the browser.");
        var connectionButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var (title, command) in new[] { ("Save / Connect", vm.ConnectCommand), ("Test Connection", vm.TestCommand), ("Refresh Models", vm.RefreshModelsCommand), ("Remove Credential", vm.RemoveKeyCommand) })
            connectionButtons.Children.Add(new Button { Content = title, Command = command, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 0) });
        ai.Children.Add(connectionButtons);
        var connectionStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        connectionStatus.SetBinding(TextBlock.TextProperty, new Binding("ConnectionStatus")); ai.Children.Add(connectionStatus);
        var modelRow = Row(ai, "Available models (refresh to discover)");
        var model = new ComboBox { ItemsSource = vm.Models, DisplayMemberPath = "Name", SelectedValuePath = "Id", Padding = new Thickness(5) };
        model.SetBinding(ComboBox.SelectedValueProperty, new Binding("Model") { Mode = BindingMode.TwoWay }); Grid.SetColumn(model, 1); modelRow.Children.Add(model);
        Note(ai, "Models are discovered from your account. For Groq screenshots choose a vision model; web search requires a model with provider search support. Choose a model after connecting, then Save settings.");
        var runtime = new StackPanel(); Field(runtime, "Gemini Python executable (optional)", "GeminiPythonPath");
        runtime.SetBinding(VisibilityProperty, new Binding("IsGemini") { Converter = new BooleanToVisibilityConverter() }); ai.Children.Add(runtime);
        Choice(ai, "Response mode", "ResponseMode", Enum.GetValues<ResponseMode>());
        Check(ai, "Allow web search when current information is needed", "WebSearch");
        Field(ai, "Maximum answer characters (16–12000)", "MaxResponseLength"); Field(ai, "Output token budget (128–8192)", "MaxOutputTokens");
        Field(ai, "Request timeout (seconds, 10–300)", "RequestTimeoutSeconds");
        var keyboard = Tab(tabs, "Keyboard");
        Note(keyboard, "Use Ctrl+Shift, Alt, or Win combinations. Ctrl+C/V/X/Z remain ordinary application shortcuts. Windows may reserve a shortcut (notably Ctrl+Shift+S); change it if registration fails.");
        Field(keyboard, "Clipboard → AI", "ClipboardShortcut"); Field(keyboard, "Select screen region", "ScreenShortcut");
        Field(keyboard, "Hide / cancel response", "HideShortcut"); Field(keyboard, "Enable / disable", "ToggleShortcut");
        Field(keyboard, "Expand last response", "ExpandShortcut");
        var privacy = Tab(tabs, "Privacy");
        Check(privacy, "Enable clipboard and browser text processing", "ClipboardEnabled"); Check(privacy, "Enable screenshot processing", "ScreenshotEnabled");
        Check(privacy, "Enable AI network requests", "NetworkEnabled");
        var ephemeral = Check(privacy, "Clear temporary screenshots after processing (always enabled)", "ClearTemporaryScreenshots"); ephemeral.IsEnabled = false;
        Note(privacy, "Clipboard is read only when you invoke an action. Screenshots are captured only after a selected region. No background monitoring, history, or disk screenshots. Inputs go only to your selected provider. Gemini uses temporary chats; local cookie caching and background refresh are disabled. Provider retention policies still apply.");
        Note(privacy, "Secure desktops and protected surfaces are respected. Blocked captures may appear blank; the assistant does not attempt to bypass restrictions.");
        Closed += (_, _) => { password.Clear(); vm.Dispose(); };
    }
    private static StackPanel Tab(TabControl tabs, string title)
    {
        var panel = new StackPanel { Margin = new Thickness(16) };
        tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        return panel;
    }
    private static Grid Row(StackPanel panel, string label)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(310) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0) });
        panel.Children.Add(grid); return grid;
    }
    private static Binding Bind(string property) => new("Settings." + property) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnExceptions = true };
    private static void Field(StackPanel panel, string label, string property)
    { var row = Row(panel, label); var text = new TextBox { Padding = new Thickness(5) }; text.SetBinding(TextBox.TextProperty, Bind(property)); Grid.SetColumn(text, 1); row.Children.Add(text); }
    private static void Choice(StackPanel panel, string label, string property, System.Collections.IEnumerable choices)
    { var row = Row(panel, label); var combo = new ComboBox { ItemsSource = choices, Padding = new Thickness(5) }; combo.SetBinding(ComboBox.SelectedItemProperty, Bind(property)); Grid.SetColumn(combo, 1); row.Children.Add(combo); }
    private static CheckBox Check(StackPanel panel, string text, string property)
    { var check = new CheckBox { Content = text, Margin = new Thickness(0, 5, 0, 12) }; check.SetBinding(CheckBox.IsCheckedProperty, Bind(property)); panel.Children.Add(check); return check; }
    private static void Note(StackPanel panel, string text) => panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap,
        Foreground = Brushes.DimGray, Margin = new Thickness(0, 2, 0, 18), LineHeight = 20 });
    private static bool HasValidationError(DependencyObject root)
    {
        if (Validation.GetHasError(root)) return true;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) if (HasValidationError(VisualTreeHelper.GetChild(root, i))) return true;
        return false;
    }
}
