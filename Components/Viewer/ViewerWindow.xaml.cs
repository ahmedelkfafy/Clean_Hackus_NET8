using Wpf.Ui.Controls;

namespace Clean_Hackus_NET8.Components.Viewer;

public partial class ViewerWindow : FluentWindow
{
    public ViewerWindow()
    {
        InitializeComponent();
        DataContext = new ViewerViewModel(EmailWebView);
    }
}
