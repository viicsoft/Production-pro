using System.Windows.Controls;
using Core;

namespace Desktop
{
    public partial class PalettesView : UserControl
    {
        private readonly IAtemSwitch _switcher;

        public PalettesView(IAtemSwitch switcher)
        {
            InitializeComponent();
            _switcher = switcher;
        }
    }
}
