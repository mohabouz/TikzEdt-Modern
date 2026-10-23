using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TikzEdt.Modern;

internal sealed partial class LaboEditorWindow
{
    private bool _allOptions;
    private void Properties()
    {
        _properties.Children.Clear();
        if(_selectedTubes.Count==1 && _selected.Count==0) { TubeProperties(); return; }
        void Text(string text) => _properties.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,5,3,5)});
        if(_selected.Count!=1) {Text(_selected.Count==0?"Select apparatus or tubing to edit it.":_selected.Count+" apparatus selected · drag to move together");return;}
        var item=_scene.Items[_selected.Single()]; Text(LaboScene.Definition(item).Title);
        var all=new CheckBox {Content="Show all package keys",IsChecked=_allOptions,Margin=new Thickness(3,5,3,8),ToolTip="Includes keys ignored by this apparatus. Unused upstream switches are explicitly identified."};
        all.Click+=(_,_)=>{_allOptions=all.IsChecked==true;Properties();}; _properties.Children.Add(all);
        foreach(var group in LaboProperties.All.Where(p=>_allOptions || LaboProperties.Applies(item,p)).GroupBy(p=>p.Group))
        {
            var fields=new StackPanel {Margin=new Thickness(4)};
            foreach(var property in group)
            {
                var value=item.Options.GetValueOrDefault(property.Key,LaboProperties.Default(item,property));
                void Commit(string text)
                {
                    if(text==value)return;
                    try {SetProperty(item.Id,property.Key,text);value=text;if(property.Kind=="Html" || LaboProperties.ExclusiveAttachments.Contains(property.Key) && text=="true")Properties();}
                    catch(Exception exception) {_status.Text=exception.Message;}
                }
                var note=property.Note+(LaboProperties.Applies(item,property)?"":" This apparatus may ignore this option.");
                if(property.Kind=="Boolean")
                {
                    var box=new CheckBox {Content=property.Label,IsChecked=value=="true",ToolTip=property.Key+". "+note,Margin=new Thickness(2,5,2,5)};
                    box.Click+=(_,_)=>Commit(box.IsChecked==true?"true":"false"); fields.Children.Add(box);continue;
                }
                var label=property.Label;
                if(property.Kind is "Number" or "Decimal") label+=$" ({property.Min}…{(item.Type is "separation" or "separation-only" && property.Key.StartsWith("niveauLiquide",StringComparison.Ordinal)?16:property.Max)})";
                fields.Children.Add(new TextBlock {Text=label,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(2,7,2,3),ToolTip=property.Key+". "+note});
                if(property.Kind is "Choice" or "Style")
                {
                    var choices=new ComboBox {ItemsSource=property.Choices,SelectedItem=value,ToolTip=property.Key};
                    choices.SelectionChanged+=(_,_)=>{if(choices.SelectedItem is string selected) Commit(selected);}; fields.Children.Add(choices);continue;
                }
                var row=new DockPanel(); var input=new TextBox {Text=value,Tag=property.Key,ToolTip=property.Key+". "+note};
                input.LostKeyboardFocus+=(_,_)=>Commit(input.Text);
                input.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter) {Commit(input.Text);e.Handled=true;}};
                if(property.Kind is "Html" or "Color")
                {
                    var pick=new Button {Content="…",Width=30,Padding=new Thickness(4),ToolTip="Choose colour"}; DockPanel.SetDock(pick,Dock.Right);row.Children.Add(pick);
                    pick.Click+=(_,_)=>
                    {
                        var dialog=new ColorPickerWindow {Owner=this};
                        if(dialog.ShowDialog()!=true)return;
                        var rgb=dialog.RgbValues.Split(',').Select(byte.Parse).ToArray();var hex=$"{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
                        input.Text=(property.Kind=="Color"?"#":"")+hex;Commit(input.Text);
                        if(property.Kind=="Html") {var index=property.Key[^1]; SetProperty(item.Id,"aspectLiquide"+index,"Custom");Properties();}
                    };
                }
                row.Children.Add(input);fields.Children.Add(row);
                if(property.Kind is "Substance" or "Solid")
                {
                    var presets=new ComboBox {ItemsSource=property.Choices,ToolTip="Presets; the field above also accepts a count, e.g. \\pstBullesChampagne[20]"};
                    presets.SelectionChanged+=(_,_)=>{if(presets.SelectedItem is string selected) {input.Text=selected;Commit(selected);}};fields.Children.Add(presets);
                }
                if(property.Note.Length>0) fields.Children.Add(new TextBlock {Text=property.Note,TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=(Brush)FindResource("MutedBrush")});
            }
            var title=group.Key;
            if(item.Type is "separation" or "separation-only") title=group.Key=="Liquid 1"?"Lower / aqueous phase":group.Key=="Liquid 2"?"Upper / organic phase":title;
            _properties.Children.Add(new Expander {Header=title,Content=fields,IsExpanded=group.Key is "Vessel" or "Liquid 1" or "Liquid 2",Margin=new Thickness(0,4,0,4)});
        }
        Text("Liquids are independent. Generic vessels allow three layers; the separating funnel has two. Fill levels use the package’s own scale, not a physical volume model.");
    }
    internal void SetProperty(string itemId,string key,string value)
    {
        var index=_scene.Items.FindIndex(i=>i.Id==itemId);if(index<0)return;
        var item=_scene.Items[index]; LaboProperties.Validate(item,key,value);
        if(item.Options.GetValueOrDefault(key,LaboProperties.Default(item,LaboProperties.All.Single(p=>p.Key==key)))==value && (!key.StartsWith("$color",StringComparison.Ordinal) || item.Options.GetValueOrDefault("aspectLiquide"+key[^1])=="Custom"))return;
        var options=new Dictionary<string,string>(item.Options){[key]=value};
        if(key.StartsWith("$color",StringComparison.Ordinal))options["aspectLiquide"+key[^1]]="Custom";
        if(value=="true" && LaboProperties.ExclusiveAttachments.Contains(key))foreach(var other in LaboProperties.ExclusiveAttachments.Where(k=>k!=key))options[other]="false";
        SaveUndo();_scene.Items[index]=item with {Options=options};Changed(false);
    }
}
