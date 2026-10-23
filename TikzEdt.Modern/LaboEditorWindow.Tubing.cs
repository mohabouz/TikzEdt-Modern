using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TikzEdt.Modern;

internal sealed partial class LaboEditorWindow
{
    private readonly HashSet<string> _selectedTubes=[];
    private readonly ToggleButton _tubeTool=new(){Content="Tube",Margin=new Thickness(4),Padding=new Thickness(8,4,8,4),ToolTip="Click two endpoints. Click apparatus to attach; click empty space for free tubing. Escape cancels."};
    private readonly ComboBox _tubeRoute=new(){ItemsSource=new[]{"Straight","Elbow","Curve"},SelectedIndex=1,Width=88};
    private LaboEnd? _tubeStart;
    private Point? _tubePointer;
    private (string Id,int Part,Point Start,LaboScene Before)? _tubeDrag;
    private void BuildTubeTools(Panel tools)
    {
        tools.Children.Add(_tubeTool);tools.Children.Add(_tubeRoute);
        _tubeTool.Click+=(_,_)=>{CancelTubeGesture();CancelDrag();_view.HandButton.IsChecked=false;Draw();};
    }
    private Point World(Point p) => new((p.X-_view.Origin.X)/(Unit*_view.Scale),(_view.Origin.Y-p.Y)/(Unit*_view.Scale));
    private Point Screen(Point p)=>Screen(p.X,p.Y);
    internal LaboEnd EndpointAt(Point screen)
    {
        var nearest=12d;LaboEnd? end=null;
        foreach(var item in _scene.Items.Where(i=>i.Ink is not null)) foreach(var port in new[]{"top","bottom","left","right"})
        {
            var candidate=new LaboEnd(item.Id,port,0,0);var distance=(Screen(_scene.Resolve(candidate))-screen).Length;
            if(distance<nearest){nearest=distance;end=candidate;}
        }
        if(end is not null)return end;
        var point=World(screen);
        var hit=_scene.Items.LastOrDefault(i=>i.Ink is not null && Bounds(i).Contains(screen));
        if(hit is not null)return new(hit.Id,"custom",point.X-hit.X,point.Y-hit.Y);
        if(_snap.IsChecked==true && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) point=new(Math.Round(point.X*2)/2,Math.Round(point.Y*2)/2);
        return new(null,"custom",point.X,point.Y);
    }
    internal void AddTube(LaboEnd start,LaboEnd end,string route)
    {
        if(_scene.Tubes.Count>=200)throw new InvalidOperationException("Maximum 200 tubes.");
        if((_scene.Resolve(start)-_scene.Resolve(end)).Length<.01)return;
        SaveUndo();var tube=new LaboTube(start,end,route);_scene.Tubes.Add(tube);_selected.Clear();_selectedTubes.Clear();_selectedTubes.Add(tube.Id);Changed();
    }
    private bool TubeDown(Point point)
    {
        if(_tubeTool.IsChecked==true)
        {
            if(_tubeStart is null){_tubeStart=EndpointAt(point);_tubePointer=point;_status.Text="Click the other endpoint. Apparatus attachments follow movement; Escape cancels.";}
            else {var start=_tubeStart;_tubeStart=null;_tubePointer=null;AddTube(start,EndpointAt(point),(string)_tubeRoute.SelectedItem);}Draw();return true;
        }
        foreach(var tube in _scene.Tubes.Where(t=>_selectedTubes.Contains(t.Id)))
        {
            var points=_scene.TubePoints(tube);var control=new Point(tube.ControlX??(points[0].X+points[^1].X)/2,tube.ControlY??(points[0].Y+points[^1].Y)/2);
            foreach(var (position,part) in new[]{(points[0],0),(points[^1],1),(control,2)})
                if((Screen(position)-point).Length<9 && (part!=2 || tube.Route!="Straight")) {_tubeDrag=(tube.Id,part,point,_scene.Clone());_view.Surface.CaptureMouse();return true;}
        }
        var hit=_scene.Tubes.LastOrDefault(t=>TubeGeometry(t).StrokeContains(new Pen(Brushes.Black,Math.Max(9,t.Diameter*Unit*_view.Scale+4)),point));
        if(hit is null)return false;
        if(!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){_selected.Clear();_selectedTubes.Clear();}
        if(!_selectedTubes.Add(hit.Id))_selectedTubes.Remove(hit.Id);Draw();Properties();return true;
    }
    private bool TubeMove(Point point,bool down)
    {
        if(_tubeStart is not null){_tubePointer=point;Draw();return true;}
        if(_tubeDrag is not {} drag || !down)return false;
        if((drag.Start-point).Length<3)return true;
        var index=_scene.Tubes.FindIndex(t=>t.Id==drag.Id);if(index<0)return true;
        var tube=_scene.Tubes[index];
        if(drag.Part==0)tube=tube with {Start=EndpointAt(point)};
        else if(drag.Part==1)tube=tube with {End=EndpointAt(point)};
        else {var world=World(point);tube=tube with {ControlX=world.X,ControlY=world.Y};}
        _scene.Tubes[index]=tube;Draw();return true;
    }
    private bool TubeUp()
    {
        if(_tubeDrag is not {} drag)return false;_tubeDrag=null;_view.Surface.ReleaseMouseCapture();
        if(!drag.Before.Tubes.SequenceEqual(_scene.Tubes)){_undo.Push(drag.Before);_redo.Clear();Changed();}return true;
    }
    private void CancelTubeGesture()
    {
        if(_tubeDrag is {} drag)_scene=drag.Before;
        _tubeDrag=null;_tubeStart=null;_tubePointer=null;_view.Surface.ReleaseMouseCapture();
    }
    private Geometry TubeGeometry(LaboTube tube)
    {
        var steps=LaboTubePath.Steps(_scene,tube);var geometry=new StreamGeometry();
        using(var context=geometry.Open())
        {
            context.BeginFigure(Screen(steps[0].End),false,false);
            foreach(var step in steps.Skip(1))
                if(step.C1 is {} c1 && step.C2 is {} c2)context.BezierTo(Screen(c1),Screen(c2),Screen(step.End),true,false);
                else context.LineTo(Screen(step.End),true,false);
        }
        geometry.Freeze();return geometry;
    }
    private void DrawTubes()
    {
        void Marker(Point p,double size,Brush color)
        {var mark=new Ellipse {Width=size,Height=size,Fill=Brushes.White,Stroke=color,StrokeThickness=1,IsHitTestVisible=false};Canvas.SetLeft(mark,p.X-size/2);Canvas.SetTop(mark,p.Y-size/2);_view.Surface.Children.Add(mark);}
        foreach(var tube in _scene.Tubes)
        {
            var geometry=TubeGeometry(tube);var width=tube.Diameter*Unit*_view.Scale;
            _view.Surface.Children.Add(new System.Windows.Shapes.Path {Data=geometry,Stroke=Brushes.Black,StrokeThickness=width,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,IsHitTestVisible=false});
            _view.Surface.Children.Add(new System.Windows.Shapes.Path {Data=geometry,Stroke=Brushes.White,StrokeThickness=Math.Max(.005,tube.Diameter-.04)*Unit*_view.Scale,StrokeLineJoin=PenLineJoin.Round,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,IsHitTestVisible=false});
            if(_selectedTubes.Contains(tube.Id))
            {
                _view.Surface.Children.Add(new System.Windows.Shapes.Path {Data=geometry,Stroke=Brushes.DodgerBlue,StrokeThickness=1,StrokeDashArray=new DoubleCollection{4,3},IsHitTestVisible=false});
                var points=_scene.TubePoints(tube);Marker(Screen(points[0]),8,Brushes.DodgerBlue);Marker(Screen(points[^1]),8,Brushes.DodgerBlue);
                if(tube.Route!="Straight")Marker(Screen(new Point(tube.ControlX??(points[0].X+points[^1].X)/2,tube.ControlY??(points[0].Y+points[^1].Y)/2)),7,Brushes.DarkOrange);
            }
        }
        if(_tubeTool.IsChecked==true)foreach(var item in _scene.Items.Where(i=>i.Ink is not null))foreach(var port in new[]{"top","bottom","left","right"}) Marker(Screen(_scene.Resolve(new(item.Id,port,0,0))),5,Brushes.SteelBlue);
        if(_tubeStart is not null && _tubePointer is {} pointer)
        {var a=Screen(_scene.Resolve(_tubeStart));_view.Surface.Children.Add(new Line {X1=a.X,Y1=a.Y,X2=pointer.X,Y2=pointer.Y,Stroke=Brushes.DodgerBlue,StrokeDashArray=new DoubleCollection{3,3},IsHitTestVisible=false});}
    }
    private void TubeProperties()
    {
        var id=_selectedTubes.Single();var tube=_scene.Tubes.Single(t=>t.Id==id);
        void Label(string text)=>_properties.Children.Add(new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(3,7,3,3)});
        void Change(Func<LaboTube,LaboTube> update)
        {var index=_scene.Tubes.FindIndex(t=>t.Id==id);if(index<0)return;var value=update(_scene.Tubes[index]);if(value==_scene.Tubes[index])return;SaveUndo();_scene.Tubes[index]=value;Changed(false);}
        Label("Tubing");var route=new ComboBox {ItemsSource=new[]{"Straight","Elbow","Curve"},SelectedItem=tube.Route};route.SelectionChanged+=(_,_)=>{if(route.SelectedItem is string r)Change(t=>t with {Route=r});};_properties.Children.Add(route);
        void Number(string name,double value,double min,double max,Func<LaboTube,double,LaboTube> update)
        {
            Label(name);var text=new TextBox {Text=LaboScene.Number(value)};_properties.Children.Add(text);
            text.LostKeyboardFocus+=(_,_)=>{if(double.TryParse(text.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var number)&&double.IsFinite(number)&&number>=min&&number<=max)Change(t=>update(t,number));else _status.Text=$"{name}: use {min}…{max}.";};
        }
        Number("Outside diameter (grid units)",tube.Diameter,.03,1,(t,n)=>t with {Diameter=n});Number("Elbow bend radius",tube.Bend,0,5,(t,n)=>t with {Bend=n});
        foreach(var start in new[]{true,false})
        {
            var end=start?tube.Start:tube.End;Label((start?"Start: ":"End: ")+(end.ItemId is null?"free":LaboScene.Definition(_scene.Items.Single(i=>i.Id==end.ItemId)).Title+" · "+end.Port));
            Number("X "+(end.ItemId is null?"position":"offset"),end.X,-500,500,(t,n)=>start?t with {Start=t.Start with {X=n}}:t with {End=t.End with {X=n}});
            Number("Y "+(end.ItemId is null?"position":"offset"),end.Y,-500,500,(t,n)=>start?t with {Start=t.Start with {Y=n}}:t with {End=t.End with {Y=n}});
            if(end.ItemId is not null){var detach=new Button {Content="Detach endpoint"};detach.Click+=(_,_)=>{Change(t=>{var point=_scene.Resolve(start?t.Start:t.End);var free=new LaboEnd(null,"custom",point.X,point.Y);return start?t with {Start=free}:t with {End=free};});Properties();};_properties.Children.Add(detach);}
        }
        Label("Drag the round endpoint handles to reconnect. Orange handle changes routing. Apparatus attachment points move with their apparatus.");
    }
}
