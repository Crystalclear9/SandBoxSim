using System;
using System.Linq;
using Godot;
using SandBoxSim.Core;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private void ValidateResidentEditing()
    {
        int original=SelectedSlot;if(original<0)return;
        string before=StateHash.ComputeDigest(Sim);long identity=_selectedPersonId;
        string name=Sim.Agents.NameOrOverride(original);
        ShowJournal(true);_drawer.CurrentTab=1;SetNameEditing(true);
        if(!_residentName.HasFocus()||_residentName.Text!=name||!_residentName.HasSelection())
            throw new InvalidOperationException("Rename does not focus and select the current resident's name");
        foreach(string blank in new[]{"","   "})
        {
            _residentName.Text=blank;
            GetViewport().PushInput(new InputEventKey {Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=true},true);
            GetViewport().PushInput(new InputEventKey {Keycode=Key.Enter,PhysicalKeycode=Key.Enter,Pressed=false},true);
            if(!_editingResidentName||!_residentNameControls.Visible||!_residentName.HasFocus()||!_contextPanel.Visible
                ||!_status.Text.StartsWith("名字不能为空",StringComparison.Ordinal)||Sim.Agents.NameOrOverride(original)!=name)
                throw new InvalidOperationException("Empty or whitespace-only name closes editing, changes the resident or hides validation feedback");
        }
        _residentName.Text="尚未保存的名字";
        // Route a real key through Godot's focus/input pipeline; do not invoke
        // a button's Pressed signal and call it an input test.
        GetViewport().PushInput(new InputEventKey {Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=true},true);
        GetViewport().PushInput(new InputEventKey {Keycode=Key.Escape,PhysicalKeycode=Key.Escape,Pressed=false},true);
        if(_editingResidentName||_residentNameControls.Visible||!_journalPanel.Visible||!_renameButton.HasFocus()
            ||Sim.Agents.NameOrOverride(original)!=name)
            throw new InvalidOperationException("Escape does not cancel the name draft before closing the notebook");
        int other=Sim.Agents.AliveSlots().FirstOrDefault(slot=>slot!=original,-1);
        if(other>=0)
        {
            string otherName=Sim.Agents.NameOrOverride(other);SetNameEditing(true);_residentName.Text="属于前一个人的草稿";
            InspectActor(other);
            if(_editingResidentName||_residentNameControls.Visible||_residentName.Text.Length!=0
                ||Sim.Agents.NameOrOverride(other)!=otherName||Sim.Agents.NameOrOverride(original)!=name)
                throw new InvalidOperationException("Changing resident selection carries a name draft into a different identity");
            InspectActor(original);
        }
        if(_selectedPersonId!=identity||before!=StateHash.ComputeDigest(Sim))throw new InvalidOperationException("Cancelling or changing name drafts modifies the world");
        RefreshResidentVitals();RefreshPortraitFacts();ValidateResidentReadouts();
        GD.Print("RESIDENT_EDITING_PASS: focused/current-name entry, routed blank-name validation/Escape cancellation, identity-bound draft and unchanged simulation");
    }
}
