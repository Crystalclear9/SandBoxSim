using System;
using System.IO;
using SandBoxSim.Core;
using SandBoxSim.Core.Save;

namespace SandBoxSim.Client;

public partial class MainGame
{
    private void ValidateSmallWorldRestore()
    {
        string original=EncodeClientWorld(true),digest=StateHash.ComputeDigest(Sim);
        int previousX=_selectedX,previousY=_selectedY;
        string path=Path.Combine(Path.GetTempPath(),"sandboxsim-small-world-"+Guid.NewGuid().ToString("N")+".simsave");
        var small=new Simulation(Sim.Config.Clone(),24,20,884);
        string snapshot=SaveFile.Encode(small),smallDigest=StateHash.ComputeDigest(small);
        try
        {
            _selectedX=79;_selectedY=79;File.WriteAllText(path,snapshot);LoadWorld(path);
            if(Sim.World.Width!=24||Sim.World.Height!=20||StateHash.ComputeDigest(Sim)!=smallDigest||!_status.Text.StartsWith("已恢复"))
                throw new InvalidOperationException("Valid smaller world reports a failed or partial load");
            if(!Sim.World.IsInBounds(SelectedX,SelectedY))throw new InvalidOperationException("Smaller world retains an out-of-bounds selection");
            _discovery.Refresh();FocusSelectedHome();FocusSelectedFarm();
            int selectedX=SelectedX,selectedY=SelectedY;LookAtPlace(4000,4000);
            if(SelectedX!=selectedX||SelectedY!=selectedY)throw new InvalidOperationException("Invalid place link replaces the safe land selection");
            _selectedX=79;_selectedY=79;_checkpoint=snapshot;RestoreCheckpoint();
            if(!Sim.World.IsInBounds(SelectedX,SelectedY)||StateHash.ComputeDigest(Sim)!=smallDigest||!_status.Text.StartsWith("回到 "))
                throw new InvalidOperationException("Small checkpoint failed to restore a safe selected tile");
            _discovery.Refresh();
        }
        finally
        {
            File.WriteAllText(path,original);LoadWorld(path);_selectedX=previousX;_selectedY=previousY;FitSelectionToWorld();
            File.Delete(path);
        }
        if(StateHash.ComputeDigest(Sim)!=digest)throw new InvalidOperationException("Small-world restore validation changed the original world");
        Godot.GD.Print("SMALL_WORLD_RESTORE_PASS: smaller load/checkpoint, safe land selection, invalid place links and restored simulation");
    }
}
