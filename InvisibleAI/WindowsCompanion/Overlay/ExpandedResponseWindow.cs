using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using InvisibleAI.Companion.AI;

namespace InvisibleAI.Companion.Overlay;

public sealed class ExpandedResponseWindow : Window
{
    public ExpandedResponseWindow(AIAnswer answer)
    {
        Title = "Invisible AI Assistant · Response"; Width = 540; Height = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var sources = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(sources, Dock.Bottom); root.Children.Add(sources);
        foreach (var source in answer.Sources)
        {
            var link = new Hyperlink(new Run(source.Title)) { NavigateUri = new Uri(source.Url) };
            link.RequestNavigate += (_, e) => { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; };
            sources.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 3, 0, 0) });
        }
        root.Children.Add(new TextBox { Text = answer.Text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 15,
            BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
    }
}
