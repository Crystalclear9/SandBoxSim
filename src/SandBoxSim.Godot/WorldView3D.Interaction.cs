using System;
using System.Linq;
using Godot;
using SandBoxSim.Core.Systems;
using SandBoxSim.Core.Agents;
namespace SandBoxSim.Client;

public partial class WorldView3D
{
    internal void CancelPointerGesture()
    {
        _orbit=_pan=_painting=_leftHeld=_leftDragged=_doubleClick=false;
        _lastPaint=new(-1,-1);
    }
    private void ReconcilePointerButtons(MouseButtonMask buttons)
    {
        // A release outside the window may never reach this Control. The next
        // motion contains the authoritative button state, so it must not paint
        // or move the camera using a stale press.
        if((buttons&MouseButtonMask.Right)==0)_orbit=false;
        if((buttons&MouseButtonMask.Middle)==0)_pan=false;
        if((buttons&MouseButtonMask.Left)==0)
        {
            _leftHeld=_painting=_leftDragged=_doubleClick=false;
            _lastPaint=new(-1,-1);
        }
    }
    private void Pan(Vector2 delta)
    {
        _follow=-1;
        // Project onto the focus plane: the point under the pointer stays attached
        // to the drag at every pitch, window size and zoom level.
        bool OnPlane(Vector2 p,out Vector3 point)
        {
            var origin=_camera.ProjectRayOrigin(p);var ray=_camera.ProjectRayNormal(p);point=Vector3.Zero;
            if(ray.Y>=-.04f)return false;
            float t=(_target.Y-origin.Y)/ray.Y;if(t<0||t>_distance*8)return false;
            point=origin+ray*t;return true;
        }
        Vector3 movement;
        if(OnPlane(_mouse-delta,out var previous)&&OnPlane(_mouse,out var current))movement=previous-current;
        else
        {
            float units=2*_distance*MathF.Tan(Mathf.DegToRad(_camera.Fov)*.5f)/MathF.Max(1,Size.Y);
            var right=new Vector3(MathF.Cos(_yaw),0,-MathF.Sin(_yaw));var forward=new Vector3(MathF.Sin(_yaw),0,MathF.Cos(_yaw));
            movement=(-right*delta.X-forward*delta.Y/MathF.Max(.25f,MathF.Sin(_pitch)))*units;
        }
        float maximum=_distance*.02f*delta.Length();if(movement.Length()>maximum)movement=movement.Normalized()*maximum;
        _target+=movement;
    }
    public override void _GuiInput(InputEvent input)
    {
        if(input is InputEventMouseButton b)
        {
            _mouse=b.Position;
            if(b.ButtonIndex is MouseButton.Right or MouseButton.Middle)
            {
                if(b.Pressed&&_leftHeld){_painting=false;_leftDragged=true;}
                if(b.ButtonIndex==MouseButton.Right)_orbit=b.Pressed;else _pan=b.Pressed;
            }
            if(b.Pressed&&b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                if(b.ShiftPressed&&Game.Tool!=PlayerTool.Inspect)
                {Game.AdjustBrushRadius(b.ButtonIndex==MouseButton.WheelUp?1:-1);AcceptEvent();return;}
                _leftHeld=_painting=_leftDragged=false;_lastPaint=new(-1,-1);
                var anchor=SurfaceHit(b.Position);
                _distance=Math.Clamp(_distance*(b.ButtonIndex==MouseButton.WheelUp?.85f:1/.85f),4,260);
                UpdateCamera();
                if(anchor.HasValue)
                {
                    var origin=_camera.ProjectRayOrigin(b.Position);var ray=_camera.ProjectRayNormal(b.Position);
                    var under=origin+ray*((anchor.Value.Y-origin.Y)/ray.Y);
                    _target+=anchor.Value-under;_follow=-1;
                }
            }
            if(b.ButtonIndex==MouseButton.Left&&b.Pressed)
            {
                _leftHeld=true;_leftDragged=_orbit||_pan||b.AltPressed;_leftStart=b.Position;_doubleClick=b.DoubleClick;
                _strokeTool=Game.Tool;_painting=Game.Tool!=PlayerTool.Inspect&&!_leftDragged;_lastPaint=new(-1,-1);
                if(_painting)Paint(b.Position);
            }
        }
        if(input is InputEventMouseMotion m)_mouse=m.Position;
        UpdateCamera();AcceptEvent();
    }
    public override void _Input(InputEvent input)
    {
        if(input is InputEventMouseButton b&&!b.Pressed)
        {
            if(b.ButtonIndex==MouseButton.Right)_orbit=false;
            if(b.ButtonIndex==MouseButton.Middle)_pan=false;
            if(b.ButtonIndex==MouseButton.Left&&_leftHeld)
            {
                var local=GetGlobalTransform().AffineInverse()*b.Position;
                if(!_painting&&!_leftDragged&&Game.Tool==PlayerTool.Inspect&&new Rect2(Vector2.Zero,Size).HasPoint(local)
                    &&GetViewport().GuiGetHoveredControl()==this)
                {
                    InspectAt(local,_doubleClick);
                }
                _leftHeld=_painting=_leftDragged=_doubleClick=false;_lastPaint=new(-1,-1);
            }
        }
        if(input is InputEventMouseMotion motion)ReconcilePointerButtons(motion.ButtonMask);
        if(input is InputEventMouseMotion m&&(_orbit||_pan||_leftHeld))
        {
            var inverse=GetGlobalTransform().AffineInverse();_mouse=inverse*m.Position;
            var relative=_mouse-inverse*(m.Position-m.Relative);
            if(_painting&&Game.Tool!=_strokeTool){_painting=false;_leftDragged=true;}
            if(_orbit){_follow=-1;_yaw-=relative.X*.008f;_pitch=Math.Clamp(_pitch+relative.Y*.005f,.18f,1.50f);}
            if(_pan)Pan(relative);
            if(_leftHeld&&!_painting&&!_orbit&&!_pan)
            {
                if(!_leftDragged&&_mouse.DistanceTo(_leftStart)>=6)_leftDragged=true;
                if(_leftDragged)Pan(relative);
            }
            if(_painting&&!_orbit&&!_pan)
            {
                if(GetViewport().GuiGetHoveredControl()==this)Paint(_mouse);else _lastPaint=new(-1,-1);
            }
            UpdateCamera();
        }
    }
    private void Paint(Vector2 screen)
    {
        var tile=Pick(screen);if(!tile.HasValue){_lastPaint=new(-1,-1);return;}
        if(tile.Value==_lastPaint)return;
        var start=_lastPaint.X<0?tile.Value:_lastPaint;
        foreach(var point in StrokeTiles(start,tile.Value))
            if(point!=_lastPaint){Game.ClickTile(point.X,point.Y,quiet:point!=tile.Value);_lastPaint=point;}
    }
    private static System.Collections.Generic.IEnumerable<Vector2I> StrokeTiles(Vector2I a,Vector2I b)
    {
        int dx=Math.Abs(b.X-a.X),dy=-Math.Abs(b.Y-a.Y),sx=a.X<b.X?1:-1,sy=a.Y<b.Y?1:-1,error=dx+dy;
        for(int guard=0;guard<4096;guard++)
        {
            yield return a;if(a==b)yield break;
            int twice=2*error;if(twice>=dy){error+=dy;a.X+=sx;}if(twice<=dx){error+=dx;a.Y+=sy;}
        }
    }
    private bool InspectAt(Vector2 screen,bool focus)
    {
        bool selected=InspectActorAt(screen);
        if(!selected)
        {
            if(FindBuildingAt(screen,out int building))
            {Game.ClickTile(Game.Sim.Buildings.XOf(building),Game.Sim.Buildings.YOf(building),true);selected=true;}
            else
            {
                var tile=Pick(screen);if(tile.HasValue){Game.ClickTile(tile.Value.X,tile.Value.Y,true);selected=true;}
            }
        }
        if(selected&&focus)Focus(Game.SelectedX,Game.SelectedY,48);
        return selected;
    }
    private bool InspectActorAt(Vector2 screen)
    {
        if(!FindActorAt(screen,out int person,out int animal,out int wolf))return false;
        Game.InspectActor(person,animal,wolf);return true;
    }
    private bool FindActorAt(Vector2 screen,out int person,out int animal,out int wolf)
    {
        person=animal=wolf=-1;Node3D? best=null;float bestDepth=float.MaxValue;
        var hit=SurfaceHit(screen);
        float terrainDepth=hit.HasValue?_camera.GlobalPosition.DistanceTo(hit.Value):float.MaxValue;
        bool Test(Node3D node,float lower,float upper,float breadth)
        {
            if(!node.Visible||_camera.IsPositionBehind(node.GlobalPosition+Vector3.Up*upper))return false;
            var a=_camera.UnprojectPosition(node.GlobalPosition+Vector3.Up*lower);var b=_camera.UnprojectPosition(node.GlobalPosition+Vector3.Up*upper);
            var span=b-a;float t=Math.Clamp((screen-a).Dot(span)/MathF.Max(.01f,span.LengthSquared()),0,1);
            float radius=Math.Clamp(span.Length()*breadth,6,30);
            if(screen.DistanceSquaredTo(a+span*t)>radius*radius)return false;
            float depth=_camera.GlobalPosition.DistanceTo(node.GlobalPosition+Vector3.Up*Mathf.Lerp(lower,upper,t));
            if(depth>terrainDepth+1||depth>=bestDepth)return false;
            bestDepth=depth;best=node;return true;
        }
        foreach(var pair in _people)
        {
            int slot=(int)(pair.Key&uint.MaxValue);
            if(Game.Sim.Agents.IsSlotAlive(slot)&&Game.Sim.Society.Identity(slot)==pair.Key&&Test(pair.Value,.12f,((ResidentRig)pair.Value).IsChild?1.12f:1.72f,.22f))
            {person=slot;animal=wolf=-1;}
        }
        foreach(var pair in _deer)
        {
            int slot=(int)(pair.Key&uint.MaxValue);
            if(Game.Sim.Wildlife.IsAlive(slot)&&Game.Sim.Wildlife.GenerationOf(slot)==(int)(pair.Key>>32)&&Test(pair.Value,.45f,1.65f,.55f)){animal=slot;person=wolf=-1;}
        }
        foreach(var pair in _wolves)if(Game.Sim.Predators.Wolves.Any(w=>w.Id==pair.Key)&&Test(pair.Value,.25f,1.05f,.65f)){wolf=pair.Key;person=animal=-1;}
        if(best==null)return false;
        var rayOrigin=_camera.ProjectRayOrigin(screen);var rayDirection=_camera.ProjectRayNormal(screen);
        foreach(var visual in _buildingVisuals.Values)
            if(visual.Surface.Visible&&MeshOccludes(visual.Surface.Mesh,visual.Surface.GlobalTransform,rayOrigin,rayDirection,bestDepth-.15f))return false;
        return true;
    }
    private readonly System.Collections.Generic.Dictionary<Mesh,(Vector3[] Points,int[] Indices)[]> _pickMeshes=new();
    private bool MeshOccludes(Mesh mesh,Transform3D transform,Vector3 worldOrigin,Vector3 worldRay,float limit)
        =>float.IsFinite(MeshHitDistance(mesh,transform,worldOrigin,worldRay,limit));
    private bool FindBuildingAt(Vector2 screen,out int slot)
    {
        slot=-1;var origin=_camera.ProjectRayOrigin(screen);var ray=_camera.ProjectRayNormal(screen);var ground=SurfaceHit(screen);
        float closest=ground.HasValue?origin.DistanceTo(ground.Value)+.01f:_camera.Far;
        foreach(var pair in _buildingVisuals)
        {
            if(!Game.Sim.Buildings.IsAlive(pair.Key)||Game.Sim.Buildings.GenerationOf(pair.Key)!=pair.Value.Generation)continue;
            var surface=pair.Value.Surface;float hit=MeshHitDistance(surface.Mesh,surface.GlobalTransform,origin,ray,closest);
            if(hit<closest){closest=hit;slot=pair.Key;}
        }
        return slot>=0;
    }
    private float MeshHitDistance(Mesh mesh,Transform3D transform,Vector3 worldOrigin,Vector3 worldRay,float limit)
    {
        var inverse=transform.AffineInverse();var origin=inverse*worldOrigin;var ray=inverse.Basis*worldRay;
        var bounds=mesh.GetAabb();float enter=0,exit=limit;
        bool Slab(float o,float d,float low,float high)
        {
            if(MathF.Abs(d)<.000001f)return o>=low&&o<=high;
            float a=(low-o)/d,b=(high-o)/d;if(a>b)(a,b)=(b,a);enter=MathF.Max(enter,a);exit=MathF.Min(exit,b);return enter<=exit;
        }
        if(!Slab(origin.X,ray.X,bounds.Position.X,bounds.End.X)||!Slab(origin.Y,ray.Y,bounds.Position.Y,bounds.End.Y)||!Slab(origin.Z,ray.Z,bounds.Position.Z,bounds.End.Z))return float.PositiveInfinity;
        if(!_pickMeshes.TryGetValue(mesh,out var surfaces))
        {
            surfaces=new (Vector3[],int[])[mesh.GetSurfaceCount()];
            for(int surface=0;surface<surfaces.Length;surface++)
            {
                var data=mesh.SurfaceGetArrays(surface);var vertices=data[(int)Mesh.ArrayType.Vertex].AsVector3Array();var indices=data[(int)Mesh.ArrayType.Index].AsInt32Array();
                if(indices.Length==0)indices=Enumerable.Range(0,vertices.Length).ToArray();surfaces[surface]=(vertices,indices);
            }
            _pickMeshes[mesh]=surfaces;
        }
        float closest=float.PositiveInfinity;
        foreach(var surface in surfaces)for(int i=0;i<surface.Indices.Length;i+=3)
        {
            var a=surface.Points[surface.Indices[i]];var e=surface.Points[surface.Indices[i+1]]-a;var f=surface.Points[surface.Indices[i+2]]-a;
            var cross=ray.Cross(f);float det=e.Dot(cross);if(MathF.Abs(det)<.0000001f)continue;
            var offset=origin-a;float u=offset.Dot(cross)/det;if(u<0||u>1)continue;
            var q=offset.Cross(e);float v=ray.Dot(q)/det;if(v<0||u+v>1)continue;
            float t=f.Dot(q)/det;if(t>=0&&t<limit&&t<closest)closest=t;
        }
        return closest;
    }
    private PanelContainer _hoverInfo=null!;
    private Label _hoverLabel=null!;
    private double _hoverElapsed;
    private void BuildHoverInfo()
    {
        _hoverInfo=new PanelContainer {MouseFilter=MouseFilterEnum.Ignore,Visible=false};
        _hoverInfo.AddThemeStyleboxOverride("panel",HudStyle.Box(HudStyle.Surface,6,10));
        _hoverLabel=HudStyle.Label("",13);_hoverLabel.MouseFilter=MouseFilterEnum.Ignore;_hoverInfo.AddChild(_hoverLabel);AddChild(_hoverInfo);
    }
    private void UpdateHoverInfo(double delta)
    {
        if(Game.Tool!=PlayerTool.Inspect||_leftHeld||_orbit||_pan||GetViewport().GuiGetHoveredControl()!=this)
        {_hoverInfo.Visible=false;return;}
        _hoverElapsed+=delta;
        if(_hoverElapsed>=.12)
        {
            _hoverElapsed=0;
            if(FindActorAt(_mouse,out int person,out int animal,out int wolf))
            {
                string name=person>=0?Game.Sim.Agents.NameOrOverride(person):animal>=0?"鹿":"狼";
                string doing=person>=0?ActionRegistry.DisplayNameOf(Game.Sim.Agents.ActionOf(person)):"野外活动";
                _hoverLabel.Text=name+" · "+doing+"\n单击查看 · 双击靠近";_hoverInfo.Visible=true;
            }
            else _hoverInfo.Visible=false;
        }
        _hoverInfo.Position=new(Math.Clamp(_mouse.X+18,8,MathF.Max(8,Size.X-_hoverInfo.Size.X-12)),Math.Clamp(_mouse.Y+20,80,MathF.Max(80,Size.Y-_hoverInfo.Size.Y-110)));
    }
    public void ValidateInteractionContinuity()
    {
        ValidatePoseScheduling();
        var target=_target;float distance=_distance,yaw=_yaw,pitch=_pitch;var mouse=_mouse;var previousTool=Game.Tool;int previousRadius=Game.Radius;
        string state=Game.Sim.StateDigestString();
        try
        {
            _target=new(Game.Sim.World.Width,1,Game.Sim.World.Height);_distance=40;
            foreach(float angle in new[]{.18f,.85f,1.48f})
            {
                _pitch=angle;UpdateCamera();var anchor=_target;var before=_camera.UnprojectPosition(anchor);var delta=new Vector2(23,11);
                _mouse=before+delta;Pan(delta);UpdateCamera();
                if(_camera.UnprojectPosition(anchor).DistanceTo(before+delta)>.15f)throw new InvalidOperationException($"Pan is detached at pitch {angle}; control {Size}, viewport {_viewport.Size}, expected {before+delta}, actual {_camera.UnprojectPosition(anchor)}");
            }
            _pitch=.18f;UpdateCamera();_mouse=new(Size.X*.5f,0);var horizonTarget=_target;
            Pan(new Vector2(2,2));
            if(!_target.IsFinite()||_target.DistanceTo(horizonTarget)>_distance*.06f)throw new InvalidOperationException("Horizon pan jumps or becomes nonfinite");
            var emptyTarget=_target;
            if(InspectAt(new Vector2(Size.X*.5f,-Size.Y),true)||_target!=emptyTarget)throw new InvalidOperationException("Double-clicking empty sky focuses a stale selection");
            _pitch=.85f;UpdateCamera();
            var screen=_camera.UnprojectPosition(PositionAt(43,50));var hit=SurfaceHit(screen);
            _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.WheelUp,Pressed=true,Position=screen});
            if(hit.HasValue&&_camera.UnprojectPosition(hit.Value).DistanceTo(screen)>.2f)throw new InvalidOperationException("Zoom drifts away from the pointer anchor");
            var occluder=new BoxMesh {Size=Vector3.One};var transform=new Transform3D(Basis.Identity.Scaled(new Vector3(2,3,1)),new(-2,1,3));
            if(!MeshOccludes(occluder,transform,new(-2,1,10),Vector3.Forward,6.6f)||MeshOccludes(occluder,transform,new(-2,1,10),Vector3.Forward,6)||MeshOccludes(occluder,transform,new(2,1,10),Vector3.Forward,9))
                throw new InvalidOperationException("Building occlusion loses ray depth, transformed geometry or empty space");
            _pickMeshes.Remove(occluder);
            var end=new Vector2I(41,27);var last=new Vector2I(3,5);int count=0;
            foreach(var point in StrokeTiles(last,end))
            {
                if(Math.Abs(point.X-last.X)>1||Math.Abs(point.Y-last.Y)>1)throw new InvalidOperationException("Fast brush stroke leaves a gap");
                last=point;count++;
            }
            if(last!=end||count!=39)throw new InvalidOperationException("Brush omits a stroke endpoint");
            _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,Position=screen});
            _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Right,Pressed=true,Position=screen});
            if(_painting||!_leftDragged)throw new InvalidOperationException("Orbit gesture can accidentally paint or inspect");
            var beforeLostRelease=_target;float yawBeforeLostRelease=_yaw,pitchBeforeLostRelease=_pitch;
            _pan=true;_painting=true;_lastPaint=new(4,4);_doubleClick=true;
            _Input(new InputEventMouseMotion {Position=screen+new Vector2(40,20),Relative=new(40,20),ButtonMask=0});
            if(_orbit||_pan||_leftHeld||_painting||_doubleClick||_lastPaint!=new Vector2I(-1,-1)
                ||_target!=beforeLostRelease||_yaw!=yawBeforeLostRelease||_pitch!=pitchBeforeLostRelease)
                throw new InvalidOperationException("Lost mouse release continues a gesture on unpressed motion");
            _orbit=_pan=_painting=_leftHeld=_leftDragged=_doubleClick=true;_lastPaint=new(4,4);
            _Notification((int)NotificationWMWindowFocusOut);
            if(_orbit||_leftHeld||_pan||_painting)throw new InvalidOperationException("Focus loss leaves a gesture active");
            _orbit=_pan=_leftHeld=_leftDragged=true;
            ReconcilePointerButtons(MouseButtonMask.Left);
            if(_orbit||_pan||!_leftHeld||!_leftDragged)
                throw new InvalidOperationException("Releasing camera buttons cancels a still-held left gesture");
            _orbit=_pan=_painting=_leftHeld=_leftDragged=_doubleClick=true;_lastPaint=new(4,4);
            RebuildWorld();
            if(_orbit||_pan||_painting||_leftHeld||_leftDragged||_doubleClick||_lastPaint!=new Vector2I(-1,-1))
                throw new InvalidOperationException("World replacement retains a gesture from the previous world");
            foreach(var pair in _people)
            {
                var node=pair.Value;bool visible=node.Visible;node.Visible=true;
                _target=node.Position+Vector3.Up*.8f;_distance=8;_pitch=.6f;UpdateCamera();
                var body=_camera.UnprojectPosition(node.Position+Vector3.Up*.8f);
                if(!FindActorAt(body,out int slot,out _,out _)||slot<0)throw new InvalidOperationException("Rendered resident cannot be inspected directly");
                node.Visible=visible;break;
            }
            if(Game.VisualPaused&&_people.Count>0)
            {
                foreach(var pair in _people)
                {
                    var node=pair.Value;var original=node.Transform;node.Position+=new Vector3(.2f,0,.1f);var frozen=node.Transform;
                    AnimateActors(.05f);
                    if(node.Transform!=frozen)throw new InvalidOperationException("Paused resident keeps sliding or turning");node.Transform=original;break;
                }
            }
            Game.SelectTool(PlayerTool.River);
            CancelPointerGesture();_mouse=Size*.5f;var panStart=_target;
            _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.Left,Pressed=true,AltPressed=true,Position=_mouse});
            _Input(new InputEventMouseMotion {Position=_mouse+new Vector2(20,10),Relative=new(20,10),ButtonMask=MouseButtonMask.Left,AltPressed=true});
            if(_painting||!_leftDragged||_target==panStart)throw new InvalidOperationException("Alt-drag paints or fails to pan while a brush is selected");
            CancelPointerGesture();float zoomBefore=_distance;int radiusBefore=Game.Radius;
            _GuiInput(new InputEventMouseButton {ButtonIndex=MouseButton.WheelDown,Pressed=true,ShiftPressed=true,Position=_mouse});
            if(_distance!=zoomBefore||Game.Radius!=Math.Max(0,radiusBefore-1))throw new InvalidOperationException("Shift-wheel zooms instead of adjusting the brush");
            _leftHeld=_painting=true;Game.SelectTool(PlayerTool.Food);
            if(_leftHeld||_painting)throw new InvalidOperationException("Tool switch continues a previous stroke");
        }
        finally{Game.AdjustBrushRadius(previousRadius-Game.Radius);Game.SelectTool(previousTool);_target=target;_distance=distance;_yaw=yaw;_pitch=pitch;_mouse=mouse;_leftHeld=_painting=_leftDragged=_orbit=_pan=false;UpdateCamera();}
        if(state!=Game.Sim.StateDigestString())throw new InvalidOperationException("Pointer observation changed simulation");
        GD.Print("POINTER_RECOVERY_PASS: lost releases, mixed held buttons and world rebuild cancel stale camera/brush state");
        GD.Print("INTERACTION_CONTINUITY_PASS: anchored pan/zoom, connected fast strokes, body picking/building occlusion, focus cancellation, paused roots and observer purity");
    }

}
