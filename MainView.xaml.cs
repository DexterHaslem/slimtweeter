using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace slimTweet
{
    public partial class MainView : Window
    {
        private readonly ViewModel _vm;
        public MainView()
        {
            InitializeComponent();
            DataContext = _vm = new ViewModel(this);
        }

        public void OnLoaded(object sender, RoutedEventArgs e)
        {
            _vm.OnLoaded();
        }

         public void OnClosing(object sender, CancelEventArgs e)
        {
            _vm.OnClosing();
        }
    }
}
