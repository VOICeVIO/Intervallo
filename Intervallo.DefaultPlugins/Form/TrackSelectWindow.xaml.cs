﻿using Intervallo.DefaultPlugins.Vocaloid;
using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace Intervallo.DefaultPlugins.Form
{
    /// <summary>
    /// TrackSelectWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class TrackSelectWindow : Window
    {
        public static readonly DependencyProperty TracksProperty = DependencyProperty.Register(
            nameof(Tracks),
            typeof(Track[]),
            typeof(TrackSelectWindow),
            new FrameworkPropertyMetadata(
                new Track[0],
                FrameworkPropertyMetadataOptions.AffectsArrange | FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                TracksChanged
            )
        );

        public TrackSelectWindow()
        {
            InitializeComponent();
        }

        public Track[] Tracks
        {
            get { return (Track[])GetValue(TracksProperty); }
            set { SetValue(TracksProperty, value); }
        }

        public bool Selected { get; private set; }

        public Track SelectedTrack
        {
            get
            {
                return Tracks[TrackListBox.SelectedIndex];
            }
        }

        void TrackListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Selected = true;
            Close();
        }

        void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Selected = true;
            Close();
        }

        static void TracksChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var window = d as TrackSelectWindow;
            window.TrackListBox.SelectedIndex = 0;
        }
    }
}
