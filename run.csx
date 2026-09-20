using System;
using System.IO;
using System.Text.RegularExpressions;

string file = @""C:\Production-pro-main\Production-pro-main\desktop\AudioMixerView.xaml.cs"";
string text = File.ReadAllText(file);
string pattern = @""private void ScrollViewer_PreviewMouseWheel\(object sender, MouseWheelEventArgs e\)[\s\S]*?e\.Handled = true;[^{}]*\}\s*\}"";
string replacement = @""private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer && e.Delta != 0)
            {
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift))
                {
                    if (MainScrollViewer != null) MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
                }
                else
                {
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                }
                e.Handled = true;
            }
        }"";
text = Regex.Replace(text, pattern, replacement);
File.WriteAllText(file, text);
