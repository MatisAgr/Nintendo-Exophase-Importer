using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace NintendoExophaseImporter.Settings
{
    /// <summary>
    /// Settings panel built in code (no XAML) so the project compiles with the plain
    /// dotnet SDK, without the WPF markup-compile targets that ship with Visual Studio.
    /// The DataContext is set by Playnite to the <see cref="NintendoExophaseSettingsViewModel"/>.
    /// </summary>
    public class NintendoExophaseSettingsView : UserControl
    {
        public NintendoExophaseSettingsView()
        {
            var root = new StackPanel { Margin = new Thickness(24, 18, 24, 24), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

            root.Children.Add(Title("Nintendo Exophase Importer"));
            root.Children.Add(Intro("Import Nintendo Switch / Switch 2 games, playtime, and metadata from Exophase into Playnite."));

            Section(root, "Exophase account");
            root.Children.Add(FieldLabel("Exophase username"));
            root.Children.Add(Bound(new TextBox(), "Settings.Username"));
            root.Children.Add(Description("Your global Exophase account username — not your Nintendo account name. A profile URL also works."));
            root.Children.Add(Note(
                "Requirements:\n" +
                "- Your Exophase profile must be public.\n" +
                "- Your Nintendo Switch activity log must be public (Nintendo Switch settings).\n\n" +
                "Keep your Exophase data up to date:\n" +
                "1. Open your Exophase user page.\n" +
                "2. Click the Nintendo tab → Options → Run profile sync.\n" +
                "3. Wait a moment, then run the sync from this add-on."));

            Section(root, "Platforms");
            Check(root, "Import Switch games", "Settings.ImportSwitch1");
            Check(root, "Import Switch 2 games", "Settings.ImportSwitch2");

            Section(root, "Library");
            Check(root, "Add games missing from Playnite", "Settings.AddMissingGames");
            Check(root, "Update games already in Playnite", "Settings.UpdateExistingGames");
            Check(root, "Only match games on a Switch platform  (recommended)", "Settings.OnlyMatchSwitchPlatform",
                "Avoids overwriting another version (PC, etc.) of the same game. Disable if your existing Switch games aren't tagged with a Switch platform.");

            Section(root, "Playtime");
            Check(root, "Import playtime", "Settings.ImportPlaytime");
            Check(root, "Overwrite existing playtime", "Settings.OverwriteExistingPlaytime",
                "When on, replaces the current playtime with the Exophase value. When off, keeps whichever is larger.");

            Section(root, "Other data");
            Check(root, "Import last played date", "Settings.ImportLastActivity");

            Section(root, "Metadata");
            Check(root, "Download metadata from IGDB for imported games", "Settings.DownloadMetadata");
            Check(root, "Overwrite existing metadata", "Settings.OverwriteMetadata",
                "When on, refreshes all fields from IGDB. When off, only fills fields that are still empty (e.g. a missing cover).");

            Section(root, "Filters");
            Check(root, "Skip demos", "Settings.SkipDemos");
            root.Children.Add(NumberField("Ignore games played less than", "Settings.MinPlaytimeMinutes", "minutes  (0 = import all)"));

            Section(root, null);
            root.Children.Add(Description("Run: main menu → Add-ons → Nintendo Exophase Importer → Sync Nintendo Switch playtime from Exophase."));

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root
            };
        }

        private static TextBlock Title(string text)
        {
            return new TextBlock { Text = text, FontWeight = FontWeights.Bold, FontSize = 20, Margin = new Thickness(0, 0, 0, 2) };
        }

        private static TextBlock Intro(string text)
        {
            return new TextBlock { Text = text, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        }

        private static void Section(Panel parent, string title)
        {
            if (!string.IsNullOrEmpty(title))
            {
                parent.Children.Add(new TextBlock
                {
                    Text = title,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 14,
                    Margin = new Thickness(0, 18, 0, 4)
                });
            }
            else
            {
                parent.Children.Add(new TextBlock { Margin = new Thickness(0, 10, 0, 0) });
            }

            parent.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80)),
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        private static TextBlock FieldLabel(string text)
        {
            return new TextBlock { Text = text, Margin = new Thickness(0, 0, 0, 3) };
        }

        private static TextBlock Description(string text)
        {
            return new TextBlock
            {
                Text = text,
                Opacity = 0.6,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(26, 1, 0, 6)
            };
        }

        private static Border Note(string text)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0x4A, 0x90, 0xD9)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x4A, 0x90, 0xD9)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 8, 0, 4),
                Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.95 }
            };
        }

        private static TextBox Bound(TextBox box, string path)
        {
            box.Margin = new Thickness(0, 0, 0, 4);
            box.SetBinding(TextBox.TextProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return box;
        }

        private static void Check(Panel parent, string text, string path, string description = null)
        {
            var cb = new CheckBox { Content = text, Margin = new Thickness(0, 5, 0, 0) };
            cb.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                new Binding(path) { Mode = BindingMode.TwoWay });
            parent.Children.Add(cb);

            if (!string.IsNullOrEmpty(description))
            {
                parent.Children.Add(Description(description));
            }
        }

        private static StackPanel NumberField(string label, string path, string suffix)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });

            var box = new TextBox { Width = 70, VerticalAlignment = VerticalAlignment.Center };
            box.SetBinding(TextBox.TextProperty, new Binding(path)
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            row.Children.Add(box);

            row.Children.Add(new TextBlock { Text = suffix, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
            return row;
        }
    }
}
