using System;
using System.Collections.Generic;
using System.Linq;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.Core.Systems;

/// <summary>Validated, portable project recipes. A save embeds its catalog so edits only affect new worlds.</summary>
public sealed class ProjectCatalog
{
    public const string DefaultJson = "{\n  \"version\": 1,\n  \"projects\": [\n    {\n      \"key\": \"meadow\",\n      \"name\": \"食物绿洲\",\n      \"brief\": \"改善土壤，播散粮食，再用雨水稳定生长。维护需要湿度与肥力；过量取粮会消耗土地。\",\n      \"art\": 0,\n      \"excludeForest\": true,\n      \"excludeRoad\": true,\n      \"steps\": [\n        {\n          \"action\": \"meadow\",\n          \"amount\": 0.25\n        },\n        {\n          \"action\": \"food\",\n          \"amount\": 28\n        },\n        {\n          \"action\": \"rain\",\n          \"amount\": 35\n        }\n      ],\n      \"care\": {\n        \"action\": \"food\",\n        \"amount\": 4,\n        \"minMoisture\": 0.35,\n        \"minFertility\": 0.25\n      },\n      \"harvest\": {\n        \"action\": \"extract_food\",\n        \"amount\": 9,\n        \"minMoisture\": 0.15,\n        \"minFertility\": 0.1\n      },\n      \"upkeep\": 3\n    },\n    {\n      \"key\": \"firebreak\",\n      \"name\": \"防火走廊\",\n      \"brief\": \"逐段清除燃料，形成道路。维护清除复燃和新燃料；自然演替会继续改变周边。\",\n      \"art\": 1,\n      \"steps\": [\n        {\n          \"action\": \"clear_strip\",\n          \"amount\": 0\n        },\n        {\n          \"action\": \"clear_strip\",\n          \"amount\": 0\n        },\n        {\n          \"action\": \"clear_strip\",\n          \"amount\": 0\n        }\n      ],\n      \"care\": {\n        \"action\": \"clear_strip\",\n        \"amount\": 0\n      },\n      \"harvest\": {\n        \"action\": \"clear_strip\",\n        \"amount\": 0\n      },\n      \"upkeep\": 2\n    },\n    {\n      \"key\": \"wetland\",\n      \"name\": \"湿地修复\",\n      \"brief\": \"持续涵养土壤，为相邻粮地和林地创造湿润条件。它不会代替饮水水域。\",\n      \"art\": 2,\n      \"steps\": [\n        {\n          \"action\": \"wetland\",\n          \"amount\": 0.22\n        },\n        {\n          \"action\": \"wetland\",\n          \"amount\": 0.22\n        },\n        {\n          \"action\": \"wetland\",\n          \"amount\": 0.22\n        }\n      ],\n      \"care\": {\n        \"action\": \"wetland\",\n        \"amount\": 0.08\n      },\n      \"harvest\": {\n        \"action\": \"extract_food\",\n        \"amount\": 5,\n        \"minMoisture\": 0.4,\n        \"minFertility\": 0.2\n      },\n      \"upkeep\": 3\n    },\n    {\n      \"key\": \"woodland\",\n      \"name\": \"林地复苏\",\n      \"brief\": \"分圈恢复森林。维护依赖水分；资源优先增加薪柴，同时降低湿度与植被。\",\n      \"art\": 3,\n      \"excludeRoad\": true,\n      \"noBurning\": true,\n      \"steps\": [\n        {\n          \"action\": \"forest_ring\",\n          \"amount\": 1\n        },\n        {\n          \"action\": \"forest_ring\",\n          \"amount\": 1\n        },\n        {\n          \"action\": \"forest_ring\",\n          \"amount\": 1\n        }\n      ],\n      \"care\": {\n        \"action\": \"forest\",\n        \"amount\": 1,\n        \"minMoisture\": 0.3\n      },\n      \"harvest\": {\n        \"action\": \"extract_wood\",\n        \"amount\": 8,\n        \"minMoisture\": 0.1\n      },\n      \"upkeep\": 3\n    },\n    {\n      \"key\": \"trail\",\n      \"name\": \"迁徙通道\",\n      \"brief\": \"分段铺设一条斜向小径，让可达性改变居民的选择。清理植被与资源是代价。\",\n      \"art\": 1,\n      \"steps\": [\n        {\n          \"action\": \"trail\",\n          \"amount\": 0\n        },\n        {\n          \"action\": \"trail\",\n          \"amount\": 0\n        },\n        {\n          \"action\": \"trail\",\n          \"amount\": 0\n        }\n      ],\n      \"care\": {\n        \"action\": \"trail\",\n        \"amount\": 0\n      },\n      \"harvest\": {\n        \"action\": \"trail\",\n        \"amount\": 0\n      },\n      \"upkeep\": 2\n    },\n    {\n      \"key\": \"mosaic\",\n      \"name\": \"林粮镶嵌\",\n      \"brief\": \"分区建立林地与粮地。用湿地维护支持它；干旱和集约采集会削弱产出。\",\n      \"art\": 0,\n      \"excludeRoad\": true,\n      \"noBurning\": true,\n      \"steps\": [\n        {\n          \"action\": \"mosaic\",\n          \"amount\": 6\n        },\n        {\n          \"action\": \"mosaic\",\n          \"amount\": 6\n        },\n        {\n          \"action\": \"rain\",\n          \"amount\": 40\n        }\n      ],\n      \"care\": {\n        \"action\": \"mosaic\",\n        \"amount\": 3,\n        \"minMoisture\": 0.35,\n        \"minFertility\": 0.2\n      },\n      \"harvest\": {\n        \"action\": \"extract_food\",\n        \"amount\": 7,\n        \"minMoisture\": 0.2,\n        \"minFertility\": 0.15\n      },\n      \"upkeep\": 4\n    }\n  ]\n}";
    public static readonly ProjectCatalog Default = Parse(DefaultJson);
    public IReadOnlyList<ProjectRecipe> Recipes { get; }
    private readonly string _json;
    private ProjectCatalog(List<ProjectRecipe> recipes, string json) { Recipes = recipes.AsReadOnly(); _json = json; }
    public JsonValue Encode() => JsonParser.Parse(_json);
    public static ProjectCatalog Parse(string json)
    {
        var root = JsonParser.Parse(json);
        if (root.GetInt("version") != 1 || !root.Get("projects").IsArray) { throw new ArgumentException("工程配方需要 version=1 和 projects 数组"); }
        var recipes = new List<ProjectRecipe>(); var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in root.Get("projects").Items)
        {
            string key = item.GetString("key"), name = item.GetString("name");
            if (string.IsNullOrWhiteSpace(key) || !keys.Add(key) || string.IsNullOrWhiteSpace(name) || name.Length > 24) { throw new ArgumentException("工程 key 必须唯一，名称为 1–24 字符"); }
            var steps = item.Get("steps").Items.Select(RecipeStep.Parse).ToArray();
            if (steps.Length < 1 || steps.Length > 8) { throw new ArgumentException("工程必须有 1–8 个日阶段"); }
            recipes.Add(new ProjectRecipe(key, name, item.GetString("brief"), Math.Clamp(item.GetInt("art"), 0, 3),
                item.GetBool("excludeForest"), item.GetBool("excludeRoad"), item.GetBool("noBurning"),
                steps, RecipeStep.Parse(item.Get("care")), RecipeStep.Parse(item.Get("harvest")),
                Bounded(item.GetInt("upkeep", 3), 0, 50), Bounded(item.GetInt("baseCost", 24), 0, 200),
                Bounded(item.GetInt("radiusCost", 2), 0, 20)));
        }
        if (recipes.Count < 1 || recipes.Count > 32) { throw new ArgumentException("工程数量必须为 1–32"); }
        return new ProjectCatalog(recipes, root.ToJson());
    }
    private static int Bounded(int value, int min, int max) => value >= min && value <= max ? value : throw new ArgumentException("工程费用超出范围");
}
public sealed record ProjectRecipe(string Key, string Name, string Brief, int Art, bool ExcludeForest, bool ExcludeRoad,
    bool NoBurning, RecipeStep[] Steps, RecipeStep Care, RecipeStep Harvest, int Upkeep, int BaseCost, int RadiusCost)
{
    public int Cost(int radius) => BaseCost + Math.Clamp(radius, 2, 8) * RadiusCost;
}
public sealed record RecipeStep(string Action, float Amount, float MinMoisture, float MinFertility)
{
    public static RecipeStep Parse(JsonValue v)
    {
        string action = v.GetString("action");
        if (action is not ("meadow" or "food" or "rain" or "clear_strip" or "wetland" or "forest_ring" or "forest" or "trail" or "mosaic" or "extract_food" or "extract_wood"))
            { throw new ArgumentException("未知工程动作：" + action); }
        float amount = v.GetFloat("amount"), moisture = v.GetFloat("minMoisture"), fertility = v.GetFloat("minFertility");
        if (!float.IsFinite(amount) || amount < 0 || amount > 100 || !float.IsFinite(moisture) || moisture < 0 || moisture > 1
            || !float.IsFinite(fertility) || fertility < 0 || fertility > 1) { throw new ArgumentException("工程数值无效"); }
        return new RecipeStep(action, amount, moisture, fertility);
    }
}
