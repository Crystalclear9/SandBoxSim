using System;
using System.Linq;
using System.Text;
using Godot;
using SandBoxSim.Core.History;
using SandBoxSim.Core.Systems;

namespace SandBoxSim.Client;

/// <summary>Actionable, read-only field notebook. Actual risks and personal stories lead into player choices.</summary>
public partial class DiscoveryPanel : VBoxContainer
{
    public MainGame Game { get; set; } = null!;
    private Label _place = null!, _phase = null!, _pinText = null!;
    private RichTextLabel _stories = null!;
    private Label _land = null!;
    private VBoxContainer _homeCard=null!;
    private Label _homeTitle=null!,_homeState=null!;
    private ProgressBar _homeCondition=null!;
    private RichTextLabel _homeResidents=null!;
    private Godot.Button _homeFocus=null!;
    private readonly Label[] _landValues=new Label[3];
    private readonly ProgressBar[] _landBars=new ProgressBar[3];
    private VBoxContainer _stockCard=null!;
    private readonly Label[] _stockValues=new Label[4];
    private Label _stockLife=null!;
    private VBoxContainer _farmCard=null!;
    private Label _farmTitle=null!,_farmText=null!;
    private LineEdit _landName = null!;
    private RichTextLabel _marks = null!;
    private WorldMiniMap _miniMap = null!;
    private Godot.Button _pinLink = null!;
    public override void _Ready()
    {
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; AddChild(scroll);
        var body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill }; body.AddThemeConstantOverride("separation", 12); scroll.AddChild(body);
        VBoxContainer Card()
        {
            var panel=new PanelContainer();var box=HudStyle.Box(HudStyle.Wash,8,14,false);panel.AddThemeStyleboxOverride("panel",box);body.AddChild(panel);
            var content=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};content.AddThemeConstantOverride("separation",8);panel.AddChild(content);return content;
        }
        body.AddChild(HudStyle.Label("田野 · 随手记", 10, true));
        _place = HudStyle.Label("", 27); body.AddChild(_place);
        _phase = HudStyle.Label("", 12, true); _phase.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_phase);
        _miniMap = new WorldMiniMap { Game = Game, CustomMinimumSize = new Vector2(280, 146) }; body.AddChild(_miniMap);
        _land = HudStyle.Label("", 13, true); _land.AutowrapMode = TextServer.AutowrapMode.WordSmart; body.AddChild(_land);
        var readings=new HBoxContainer();readings.AddThemeConstantOverride("separation",14);body.AddChild(readings);
        string[] names={"土壤水分","肥沃程度","活动压实"};string[] colors={"#92b5bd","#a7b899","#d4b995"};
        for(int i=0;i<3;i++)
        {
            var column=new VBoxContainer {SizeFlagsHorizontal=SizeFlags.ExpandFill};readings.AddChild(column);
            column.AddChild(HudStyle.Label(names[i],10,true));_landValues[i]=HudStyle.Label("",16);column.AddChild(_landValues[i]);
            var bar=new ProgressBar {MaxValue=1,ShowPercentage=false,CustomMinimumSize=new(0,3),MouseFilter=MouseFilterEnum.Ignore};
            bar.AddThemeStyleboxOverride("background",HudStyle.Box(HudStyle.Border,2,0,false));bar.AddThemeStyleboxOverride("fill",HudStyle.Box(new Color(colors[i]),2,0,false));
            _landBars[i]=bar;column.AddChild(bar);
        }
        _homeCard=Card();_homeCard.Visible=false;
        _homeTitle=HudStyle.Heading("住房",17);_homeCard.AddChild(_homeTitle);
        _homeCondition=new ProgressBar {MaxValue=1,ShowPercentage=false,CustomMinimumSize=new(0,4),MouseFilter=MouseFilterEnum.Ignore};
        _homeCondition.AddThemeStyleboxOverride("background",HudStyle.Box(HudStyle.Border,2,0,false));
        _homeCondition.AddThemeStyleboxOverride("fill",HudStyle.Box(HudStyle.Accent,2,0,false));_homeCard.AddChild(_homeCondition);
        _homeState=HudStyle.Label("",12,true);_homeState.AutowrapMode=TextServer.AutowrapMode.WordSmart;_homeCard.AddChild(_homeState);
        _homeResidents=new RichTextLabel {BbcodeEnabled=true,FitContent=true,ScrollActive=false};_homeCard.AddChild(_homeResidents);
        _homeResidents.MetaClicked+=meta=>{if(long.TryParse(meta.AsString(),out long person))Game.FocusStory(person,-1,-1);};
        _homeFocus=new Godot.Button {Text="近看这座房屋 →",Alignment=HorizontalAlignment.Left};HudStyle.Button(_homeFocus);_homeCard.AddChild(_homeFocus);
        _homeFocus.Pressed+=Game.FocusSelectedHome;
        _farmCard=Card();_farmTitle=HudStyle.Heading("农田",17);_farmCard.AddChild(_farmTitle);
        _farmText=HudStyle.Label("",12,true);_farmText.AutowrapMode=TextServer.AutowrapMode.WordSmart;_farmCard.AddChild(_farmText);
        var farmFocus=new Godot.Button {Text="近看农田 →",Alignment=HorizontalAlignment.Left};HudStyle.Button(farmFocus);_farmCard.AddChild(farmFocus);farmFocus.Pressed+=Game.FocusSelectedFarm;
        _stockCard=Card();_stockCard.AddChild(HudStyle.Heading("地面物资",16));
        var inventory=new GridContainer {Columns=2,SizeFlagsHorizontal=SizeFlags.ExpandFill};inventory.AddThemeConstantOverride("h_separation",20);_stockCard.AddChild(inventory);
        string[] resources={"食物","木材","石料","铁矿"};
        for(int i=0;i<4;i++){_stockValues[i]=HudStyle.Label(resources[i]+"  0",13);inventory.AddChild(_stockValues[i]);}
        _stockLife=HudStyle.Label("",11,true);_stockLife.AutowrapMode=TextServer.AutowrapMode.WordSmart;_stockCard.AddChild(_stockLife);
        var naming = new HBoxContainer(); body.AddChild(naming);
        _landName = new LineEdit { PlaceholderText = "为这里起个名字", MaxLength = 24, SizeFlagsHorizontal = SizeFlags.ExpandFill }; naming.AddChild(_landName);
        var remember = new Godot.Button { Text = "记下" }; HudStyle.Button(remember); naming.AddChild(remember);
        remember.Pressed += () => { Game.RememberPlace(_landName.Text); _landName.Text = ""; Refresh(); };
        _marks = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false }; body.AddChild(_marks);
        _marks.MetaClicked += meta => {
            string[] parts = meta.AsString().Split(':');
            if(parts.Length!=3 || !int.TryParse(parts[1],out int x) || !int.TryParse(parts[2],out int y))return;
            if(parts[0]=="forget") { Game.Wild.Forget(x,y); Refresh(); } else { Game.LookAtPlace(x,y); }
        };
        var residentCard=Card(); residentCard.AddChild(HudStyle.Heading("居民的故事", 16));
        _pinText = HudStyle.Label("", 13, true); _pinText.AutowrapMode = TextServer.AutowrapMode.WordSmart; residentCard.AddChild(_pinText);
        _pinLink = new Godot.Button { Text = "查看关注的故事 →", Alignment = HorizontalAlignment.Left }; HudStyle.Button(_pinLink); _pinLink.Pressed += () => Game.FocusPinned(); residentCard.AddChild(_pinLink);
        var storyCard=Card(); storyCard.AddChild(HudStyle.Heading("世界的回声", 16));
        _stories = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, SelectionEnabled = true, CustomMinimumSize = new Vector2(0, 80) };
        _stories.MetaClicked += meta =>
        {
            var fields = meta.AsString().Split(':');
            if (fields.Length == 3 && long.TryParse(fields[0], out long person) && int.TryParse(fields[1], out int x) && int.TryParse(fields[2], out int y)) { Game.FocusStory(person, x, y); }
        };
        storyCard.AddChild(_stories); Refresh();
    }
    public void Refresh()
    {
        if (_place == null) { return; }
        var sim = Game.Sim;
        _place.Text = SandboxScenarios.Names[Game.Scenario];
        string weather = sim.World.Weather.Kind switch { SandBoxSim.Core.Environment.WeatherKind.Clear => "晴朗", SandBoxSim.Core.Environment.WeatherKind.Cloudy => "多云",
            SandBoxSim.Core.Environment.WeatherKind.Rain => "降雨", SandBoxSim.Core.Environment.WeatherKind.Storm => "风暴",
            SandBoxSim.Core.Environment.WeatherKind.Drought => "旱季", _ => "降雪" };
        _phase.Text = weather + " · " + WildPlaces.PhaseName(sim.Clock / sim.Config.Clock.TicksPerDay) + " · 第 " + sim.World.Calendar.Day + " 天";
        var local=Game.Wild.At(Game.SelectedX,Game.SelectedY);
        var tile=sim.World.TileAt(Game.SelectedX,Game.SelectedY);
        float[] readings={tile.Moisture,tile.Fertility,tile.FootTraffic};
        for(int i=0;i<3;i++){_landValues[i].Text=readings[i].ToString("P0");_landBars[i].Value=readings[i];}
        _land.Text=local==null ? $"({Game.SelectedX}, {Game.SelectedY}) · 单击土地查看这里的水土与痕迹。" : Game.Wild.Describe(sim,local);
        int building=sim.World.TileAt(Game.SelectedX,Game.SelectedY).BuildingId-1;
        _homeCard.Visible=false;
        _homeCard.GetParent<PanelContainer>().Visible=false;
        _farmCard.GetParent<PanelContainer>().Visible=false;
        if(building>=0&&sim.Buildings.IsAlive(building))
        {
            int people=sim.Buildings.OccupiedBedsOf(building);float condition=sim.Buildings.DecayOf(building);
            if(sim.Buildings.KindOf(building)==SandBoxSim.Core.Environment.BuildingKind.House)
            {
                bool complete=sim.Buildings.StateOf(building)==SandBoxSim.Core.Environment.BuildingState.Complete;
                _homeCard.Visible=true;_homeCondition.Visible=true;_homeCondition.Value=complete?condition:sim.Buildings.ProgressOf(building);
                _homeCard.GetParent<PanelContainer>().Visible=true;
                _homeTitle.Text=complete?(people>0?$"生活中的家 · {people} 位住户":"空置的住房"):"施工中的住房";
                _homeState.Text=complete?$"建筑完整度 {condition:P0}\n"+(sim.Config.Buildings.DecayPerDay<=0?"当前世界已关闭建筑衰败。":people>0?"入住后逐渐恢复；晴朗、多云和旱季可见晾晒。":"长期空置会逐渐风化，入住后缓慢恢复。"):$"施工进度 {sim.Buildings.ProgressOf(building):P0}\n居民依据需求与材料自主施工。";
                var occupants=new StringBuilder();
                foreach(int resident in sim.Agents.AliveSlots())
                {
                    if(sim.Agents.DwellingOf(resident)!=building)continue;
                    long identity=sim.Society.Identity(resident);var person=sim.Society.Find(identity);
                    if(person!=null)occupants.AppendLine($"[url={identity}][color=#d4b995]{person.Name.Replace("[","[lb]")} →[/color][/url]");
                }
                _homeResidents.Text=occupants.ToString();_homeResidents.Visible=occupants.Length>0;
            }
            else if(sim.Buildings.KindOf(building)==SandBoxSim.Core.Environment.BuildingKind.Farm)
            {
                _farmCard.GetParent<PanelContainer>().Visible=true;
                var crop=LivingAgriculture.CropAt(sim.World.Seed,Game.SelectedX,Game.SelectedY);int phase=LivingAgriculture.Phase(sim.Clock,sim.World.Calendar.TicksPerDay);
                _farmTitle.Text=LivingAgriculture.CropName(crop)+"田 · "+WildPlaces.PhaseName(sim.Clock/sim.World.Calendar.TicksPerDay);
                _farmText.Text=sim.Buildings.StateOf(building)!=SandBoxSim.Core.Environment.BuildingState.Complete?$"施工进度 {sim.Buildings.ProgressOf(building):P0}":$"今日劳动 {sim.Buildings.LaborOf(building):F1} / {sim.Config.Buildings.FarmLaborPerDayCap:F1}\n周期产能 ×{(sim.Config.Buildings.LivingAgricultureEnabled?LivingAgriculture.YieldFactor(crop,phase):1):F2}\n"+(sim.Config.Buildings.LivingAgricultureEnabled?(crop==CropKind.Legumes?"豆科耕作帮助恢复地力。":"持续耕作消耗地力；休耕与休眠期恢复。"):"当前世界使用传统生产规则。");
            }
            else _land.Text+=$"\n建筑完整度 {condition:P0}";
        }
        int pile=sim.GroundStocks.FindAt(Game.SelectedX,Game.SelectedY);
        _stockCard.GetParent<PanelContainer>().Visible=pile>=0;
        _stockLife.Visible=pile>=0&&sim.Config.Buildings.LivingAgricultureEnabled&&sim.GroundStocks.AmountOf(pile,SandBoxSim.Core.Environment.ResourceKind.Food)>0;
        if(_stockLife.Visible)
        {
            float rate=Math.Clamp(sim.Config.Buildings.GroundFoodSpoilagePerDay*(.5f+tile.Temperature)*(1+.2f*tile.Moisture),0,1);
            _stockLife.Text=$"地面食物每日腐损约 {rate:P1}，仓库提供存放保护。";
        }
        if(pile>=0)foreach(var kind in ResidentRig.CargoKinds)
        {
            float amount=sim.GroundStocks.AmountOf(pile,kind);
            string name=kind==SandBoxSim.Core.Environment.ResourceKind.Food?"食物":kind==SandBoxSim.Core.Environment.ResourceKind.Wood?"木材":kind==SandBoxSim.Core.Environment.ResourceKind.Stone?"石料":"铁矿";
            var label=_stockValues[(int)kind-1];label.Text=$"{name}  {amount:F1}";label.Visible=amount>.01f;
        }
        float traffic=sim.World.TileAt(Game.SelectedX,Game.SelectedY).FootTraffic;
        if(traffic>.02f)_land.Text+=$"\n{(traffic>.25f?"逐渐成形的小径":"新近留下的足迹")} · 压实 {traffic:P0}\n反复行走让痕迹加深；无人经过时，土地缓慢恢复。";
        var notes=new StringBuilder();
        foreach(var mark in Game.Wild.Marks)
            notes.AppendLine($"[url=place:{mark.X}:{mark.Y}][color=#c7b992]{mark.Name.Replace("[","[lb]")}[/color][/url]  [url=forget:{mark.X}:{mark.Y}]×[/url]");
        _marks.Text=notes.ToString();
        var pinned = sim.Society.Find(Game.PinnedPerson); _pinLink.Visible = pinned != null;
        _pinText.Text = pinned == null ? "尚未关注居民。"
            : pinned.Alive ? pinned.Name + $" · 健康 {sim.Agents.HealthOf(pinned.Slot):P0}\n饥饿 {sim.Agents.HungerOf(pinned.Slot):P0} · " + (sim.Agents.DwellingOf(pinned.Slot) >= 0 ? "已有居所" : "仍在寻找家园")
            : pinned.Name + "已离世。\n他的家庭与个人历史仍保留在人物页。";
        var text = new StringBuilder();
        foreach (var ev in sim.Society.History.Where(e => IsStory((WorldEventType)e.Type)).TakeLast(3).Reverse())
        {
            text.AppendLine($"[color=#a8b3a5]第 {ev.Tick / sim.Config.Clock.TicksPerDay + 1} 天[/color]  " + ev.Description.Replace("[", "[lb]"));
            if (ev.Actor != 0 || ev.X >= 0 && ev.Y >= 0) { text.AppendLine($"[url={ev.Actor}:{ev.X}:{ev.Y}][color=#c7b992]追踪故事 →[/color][/url]"); }
            text.AppendLine();
        }
        _stories.Text = text.Length == 0 ? "[color=#a8b3a5]居民的第一次选择，将成为这里的故事。[/color]" : text.ToString();
        _miniMap.QueueRedraw();
    }
    public void ValidateNavigation() => _miniMap.ValidateMapping();
    internal void EvaluationHomeCommand(string command)
    {
        Refresh();if(!_homeCard.IsVisibleInTree())throw new InvalidOperationException("No selected home card");
        if(command=="home.focus"){_homeFocus.EmitSignal(Godot.Button.SignalName.Pressed);return;}
        int building=Game.Sim.World.TileAt(Game.SelectedX,Game.SelectedY).BuildingId-1;
        int resident=Game.Sim.Agents.AliveSlots().FirstOrDefault(i=>Game.Sim.Agents.DwellingOf(i)==building,-1);
        if(resident<0)throw new InvalidOperationException("Selected home has no resident to inspect");
        _homeResidents.EmitSignal(RichTextLabel.SignalName.MetaClicked,Game.Sim.Society.Identity(resident).ToString());
    }
    internal void ValidateHomeInteraction(int slot)
    {
        Refresh();
        if(!_homeCard.GetParent<PanelContainer>().Visible||!_homeCard.Visible)throw new InvalidOperationException("Selected home card hidden");
        _homeFocus.EmitSignal(Godot.Button.SignalName.Pressed);
        int resident=Game.Sim.Agents.AliveSlots().FirstOrDefault(i=>Game.Sim.Agents.DwellingOf(i)==slot,-1);
        if(resident>=0)
        {
            long person=Game.Sim.Society.Identity(resident);
            if(!_homeResidents.Text.Contains($"url={person}"))throw new InvalidOperationException("Home resident link missing");
            _homeResidents.EmitSignal(RichTextLabel.SignalName.MetaClicked,person.ToString());
            if(Game.SelectedSlot!=resident)throw new InvalidOperationException("Home resident link selects wrong person");
        }
    }
    private static bool IsStory(WorldEventType type) => type is WorldEventType.AgentBorn or WorldEventType.AgentDied
        or WorldEventType.Marriage or WorldEventType.BuildingCompleted or WorldEventType.SettlementFounded
        or WorldEventType.TradeRouteEstablished or WorldEventType.WarDeclared or WorldEventType.Innovation or WorldEventType.LeaderElected;
}
