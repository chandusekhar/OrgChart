﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;
using Staffer.OrgChart.CSharp.Test.Layout;
using Staffer.OrgChart.Layout.CSharp;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Staffer.OrgChart.CSharp.Test.App
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var dataSource = new TestDataSource();
            new TestDataGen().GenerateDataItems(dataSource);

            var boxContainer = new BoxContainer(dataSource);

            TestDataGen.GenerateBoxSizes(boxContainer);

            RenderBoxes(boxContainer, DrawCanvas);
        }

        private static void RenderBoxes([NotNull] BoxContainer boxContainer, [NotNull]Canvas drawCanvas)
        {
            drawCanvas.Children.Clear();

            drawCanvas.RenderTransform = new ScaleTransform
            {
                //ScaleY = 0.01
            };

            foreach (var box in boxContainer.Boxes.Values)
            {
                var frame = box.Frame;
                drawCanvas.Children.Add(new Rectangle
                {
                    RenderTransform = new TranslateTransform { X = frame.Exterior.TopLeft.X, Y = frame.Exterior.TopLeft.Y},
                    Width = frame.Exterior.Size.Width,
                    Height = frame.Exterior.Size.Height,
                    Fill = new SolidColorBrush(Colors.Beige),
                    Stroke = new SolidColorBrush(Colors.Black),
                    StrokeThickness = 1
                });
            }
        }
    }
}
