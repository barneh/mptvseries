using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace WindowPlugins.GUITVSeries
{
    public partial class loadingDisplay : Form
    {
        public loadingDisplay()
        {
            InitializeComponent();
        }

        public void ShowWaiting()
        {
            this.Show();
            this.Refresh();
        }

    }
}