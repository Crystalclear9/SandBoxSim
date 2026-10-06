using Godot;

namespace SandBoxSim.Client;
public partial class MainGame
{
    private Godot.Button _pinButton = null!, _pulseButton = null!;
    private LineEdit _residentName = null!;
    private void BuildWorldPulse(Control overlay)
    {
        _pulseButton=new Godot.Button { Text="世界 · 田野手记", Alignment=HorizontalAlignment.Left };
        HudStyle.Button(_pulseButton);
        HudStyle.Float(_pulseButton,new Vector2(1,0),new(-372,92),new(348,44)); overlay.AddChild(_pulseButton);
        _pulseButton.Pressed+=()=>{ShowJournal(true);_drawer.CurrentTab=0;};
    }
    private void RenameResident()
    {
        var person=Sim.Society.Find(_selectedPersonId); string name=_residentName.Text.Trim();
        if(person==null || name.Length==0) { _status.Text="选择人物后可以为他命名";return; }
        string previous=person.Name;person.Name=name;
        if(person.Alive)Sim.Agents.SetNameOverride(person.Slot,name);
        Sim.InterveneRecordAuxiliary("居民命名："+previous+" → "+name);RefreshPanels();_discovery.Refresh();
    }
    public void FocusLocation(int x,int y){if(Sim.World.IsInBounds(x,y))_map.Focus(x,y,20);}
    public void FocusPinned()
    {
        var person=Sim.Society.Find(PinnedPerson);
        if(person!=null)FocusStory(person.Id,person.Alive?Sim.Agents.XOf(person.Slot):-1,person.Alive?Sim.Agents.YOf(person.Slot):-1);
    }
    private void TogglePin(){if(_selectedPersonId==0)return;PinnedPerson=PinnedPerson==_selectedPersonId?0:_selectedPersonId;RefreshOperations();_discovery.Refresh();}
    private void RefreshOperations()
    {
        if(_pinButton!=null)_pinButton.Text=PinnedPerson!=0 && PinnedPerson==_selectedPersonId?"取消关注":"关注这个居民";
        if(_pulseButton!=null)_pulseButton.Text="田野手记 · "+WildPlacesLabel();
    }
    private string WildPlacesLabel()=>SandBoxSim.Core.Systems.WildPlaces.PhaseName(Sim.Clock/Sim.Config.Clock.TicksPerDay);
}
