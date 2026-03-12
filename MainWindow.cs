using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Collections.Generic;
using System.IO;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace MacUABEA;

public class AssetRow 
{ 
    public string Name { get; set; } = ""; 
    public string Type { get; set; } = ""; 
    public long PathId { get; set; }
    public AssetFileInfo? Info { get; set; }
    public override string ToString() => $"[{PathId}] {Name} ({Type})";
}

public class MainWindow : Window
{
    private ListBox _listBox;
    private TextBox _searchBox;
    private ComboBox _typeFilter;
    private TextBlock _fileStatus;
    private AssetsManager _mgr = new AssetsManager();
    private AssetsFileInstance? _currentFile;
    private List<AssetRow> _allRows = new();
    
    public MainWindow()
    {
        Title = "UABEA Mac - Pro Editor";
        Width = 1000; Height = 650;

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); 
        mainGrid.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star))); 
        mainGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var topBar = new StackPanel { 
            Background = Brushes.Black, 
            Height = 50, 
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };

        var loadBtn = new Button { Content = "Open File", Margin = new Thickness(5) };
        loadBtn.Click += OnLoadClick;

        _searchBox = new TextBox { Watermark = "Search name/ID...", Width = 200, Margin = new Thickness(5) };
        _searchBox.TextChanged += (s, e) => ApplyFilter();

        _typeFilter = new ComboBox { Width = 150, Margin = new Thickness(5) };
        _typeFilter.PlaceholderText = "All Types";
        _typeFilter.SelectionChanged += (s, e) => ApplyFilter();

        var clearBtn = new Button { Content = "Clear Filters", Margin = new Thickness(5) };
        clearBtn.Click += (s, e) => {
            _searchBox.Text = "";
            _typeFilter.SelectedIndex = -1;
        };

        _fileStatus = new TextBlock { Text = "Ready", VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.Gray };
        
        topBar.Children.Add(loadBtn);
        topBar.Children.Add(new Separator { Width = 1, Background = Brushes.DimGray });
        topBar.Children.Add(_searchBox);
        topBar.Children.Add(_typeFilter);
        topBar.Children.Add(clearBtn);
        topBar.Children.Add(_fileStatus);
        Grid.SetRow(topBar, 0);

        _listBox = new ListBox { Margin = new Thickness(5) };
        Grid.SetRow(_listBox, 1);

        var bottomPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10), Spacing = 10 };
        var viewEditBtn = new Button { Content = "View / Edit Asset", Background = Brushes.DarkSlateBlue, Padding = new Thickness(15, 5) };
        viewEditBtn.Click += OnViewDataClick;
        
        var saveBtn = new Button { Content = "Save All", Background = Brushes.DarkGreen };
        saveBtn.Click += OnSaveFileClick;

        bottomPanel.Children.Add(viewEditBtn);
        bottomPanel.Children.Add(saveBtn);
        Grid.SetRow(bottomPanel, 2);

        mainGrid.Children.Add(topBar);
        mainGrid.Children.Add(_listBox);
        mainGrid.Children.Add(bottomPanel);
        Content = mainGrid;
    }

    private void ApplyFilter()
    {
        var searchText = _searchBox.Text?.ToLower() ?? "";
        var selectedType = (_typeFilter.SelectedItem as string);

        var filtered = _allRows.AsEnumerable();

        if (!string.IsNullOrEmpty(selectedType))
            filtered = filtered.Where(r => r.Type == selectedType);

        if (!string.IsNullOrEmpty(searchText))
            filtered = filtered.Where(r => r.Name.ToLower().Contains(searchText) || r.PathId.ToString().Contains(searchText));

        _listBox.ItemsSource = filtered.ToList();
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new() { Title = "Open Assets" });
        if (files?.Count > 0)
        {
            string path = files[0].Path.AbsolutePath;
            try {
                _fileStatus.Text = "Reading types...";
                await Task.Run(() => {
                    _currentFile = _mgr.LoadAssetsFile(path, true);
                    if (_currentFile == null) return;
                    
                    var newRows = new List<AssetRow>();
                    var typeList = new HashSet<string>();

                    foreach (var info in _currentFile.file.AssetInfos)
                    {
                        string typeName = ((AssetClassID)info.TypeId).ToString();
                        typeList.Add(typeName);

                        string name = "Unnamed";
                        try {
                            var baseField = _mgr.GetBaseField(_currentFile, info);
                            if (baseField?["m_Name"] != null) name = baseField["m_Name"].AsString;
                        } catch { }
                        newRows.Add(new AssetRow { Name = name, Type = typeName, PathId = info.PathId, Info = info });
                    }
                    _allRows = newRows;

                    Dispatcher.UIThread.Post(() => {
                        _typeFilter.ItemsSource = typeList.OrderBy(t => t).ToList();
                    });
                });
                ApplyFilter();
                _fileStatus.Text = $"Loaded: {Path.GetFileName(path)}";
            } catch { _fileStatus.Text = "Load Error!"; }
        }
    }

    private void OnViewDataClick(object? sender, RoutedEventArgs e)
    {
        if (_listBox.SelectedItem is AssetRow row && _currentFile != null && row.Info != null)
        {
            AssetTypeValueField? baseField = null;
            try { baseField = _mgr.GetBaseField(_currentFile, row.Info); } catch { }
            var inspector = new InspectorWindow(baseField, row.Info, _currentFile, row.Name);
            inspector.Show();
        }
    }

    private async void OnSaveFileClick(object? sender, RoutedEventArgs e)
    {
        if (_currentFile == null) return;
        var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = "globalgamemanagers" });
        if (file != null)
        {
            using var stream = File.OpenWrite(file.Path.AbsolutePath);
            using var writer = new AssetsFileWriter(stream);
            _currentFile.file.Write(writer);
            _fileStatus.Text = "Saved!";
        }
    }
}

public class InspectorWindow : Window
{
    public InspectorWindow(AssetTypeValueField? baseField, AssetFileInfo info, AssetsFileInstance inst, string assetName)
    {
        Title = $"Inspector: {assetName}";
        Width = 750; Height = 850;
        var mainStack = new StackPanel { Margin = new Thickness(15), Spacing = 10 };
        var scroll = new ScrollViewer { Content = mainStack };

        if (baseField != null && ((AssetClassID)info.TypeId) == AssetClassID.Texture2D)
        {
            var texHeader = new Border { Background = Brushes.DarkCyan, Padding = new Thickness(10), CornerRadius = new CornerRadius(5) };
            var texStack = new StackPanel();
            texStack.Children.Add(new TextBlock { Text = "Texture2D Properties", FontWeight = FontWeight.Bold });
            
            var exportBtn = new Button { Content = "Export Raw Data (.dat)", Margin = new Thickness(0, 5, 0, 0) };
            exportBtn.Click += async (s, e) => {
                var imgDataField = baseField["image data"];
                if (imgDataField != null) {
                    var data = imgDataField.AsByteArray;
                    var file = await StorageProvider.SaveFilePickerAsync(new() { SuggestedFileName = $"{assetName}.dat" });
                    if (file != null) File.WriteAllBytes(file.Path.AbsolutePath, data);
                }
            };
            texStack.Children.Add(exportBtn);
            texHeader.Child = texStack;
            mainStack.Children.Add(texHeader);
        }

        if (baseField != null)
        {
            AddFieldsRecursive(mainStack, baseField, 0);
        }
        else
        {
            inst.file.Reader.BaseStream.Position = info.ByteOffset;
            byte[] rawData = inst.file.Reader.ReadBytes((int)info.ByteSize);
            
            mainStack.Children.Add(new TextBlock { Text = "Hex Dump (16-byte rows):", Foreground = Brushes.Yellow });
            mainStack.Children.Add(new TextBox { 
                Text = FormatHex(rawData), Height = 350, AcceptsReturn = true, 
                FontFamily = new FontFamily("Courier New"), IsReadOnly = true 
            });

            mainStack.Children.Add(new TextBlock { Text = "ASCII Preview:", Foreground = Brushes.LightBlue });
            mainStack.Children.Add(new TextBox { 
                Text = Encoding.ASCII.GetString(rawData.Select(b => (b < 32 || b > 126) ? (byte)'.' : b).ToArray()),
                Height = 150, TextWrapping = TextWrapping.Wrap, IsReadOnly = true 
            });
        }
        Content = scroll;
    }

    private string FormatHex(byte[] data)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            sb.Append(data[i].ToString("X2") + " ");
            if ((i + 1) % 16 == 0) sb.AppendLine();
        }
        return sb.ToString();
    }

    private void AddFieldsRecursive(StackPanel panel, AssetTypeValueField field, int indent)
    {
        if (field == null) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(indent * 15, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = $"{field.FieldName}: ", VerticalAlignment = VerticalAlignment.Center });
        if (field.Value != null) {
            var edit = new TextBox { Text = field.Value.AsString, Width = 250 };
            edit.TextChanged += (s, e) => { try { field.Value.AsString = edit.Text ?? ""; } catch { } };
            row.Children.Add(edit);
        }
        panel.Children.Add(row);
        if (field.Children != null) {
            foreach (var child in field.Children) AddFieldsRecursive(panel, child, indent + 1);
        }
    }
}
