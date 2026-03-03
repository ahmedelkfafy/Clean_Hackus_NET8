using System.Windows.Input;
using Wpf.Ui.Controls;

namespace Clean_Hackus_NET8.Components.Viewer;

public partial class ViewerWindow : FluentWindow
{
    public ViewerWindow()
    {
        InitializeComponent();
        DataContext = new ViewerViewModel(MessageWebView);
    }

    // Search on Enter key
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is ViewerViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
