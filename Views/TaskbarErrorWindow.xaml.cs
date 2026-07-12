using System.Windows;

namespace TaskbarMiniPlayer.Views
{
    public partial class TaskbarErrorWindow : Window
    {
        public TaskbarErrorWindow()
        {
            InitializeComponent();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
