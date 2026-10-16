using System;
using System.Linq;
using System.Threading.Tasks;
using NebulaAIChat.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NebulaAIChat.Utils
{
    public static class GameDataTools
    {
        public static JArray Definitions => JArray.Parse(@"
[
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_player_status"",
      ""description"": ""获取当前玩家机甲的实时状态：电量、HP、所在星系/星球、坐标、游戏刻。"",
      ""parameters"": { ""type"": ""object"", ""properties"": {} }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_planet_info"",
      ""description"": ""获取当前星球（或指定 planetId 的星球）的建筑数量、黑雾单位数。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""planet_id"": { ""type"": ""integer"", ""description"": ""星球 ID，不填或填 0 表示当前星球"" }
        }
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_research_status"",
      ""description"": ""获取科技研究状态：当前研究项目 ID、研究队列长度。返回的 ID 需要用 translate_name 翻译。"",
      ""parameters"": { ""type"": ""object"", ""properties"": {} }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_inventory"",
      ""description"": ""获取玩家背包物品清单（按数量降序）。返回的 item_id 需要用 translate_name 翻译。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""top_n"": { ""type"": ""integer"", ""description"": ""返回数量前 N 的物品，默认 20，最大 50"" }
        }
      }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""get_dyson_sphere_summary"",
      ""description"": ""获取所有星系戴森球的数量概况。"",
      ""parameters"": { ""type"": ""object"", ""properties"": {} }
    }
  },
  {
    ""type"": ""function"",
    ""function"": {
      ""name"": ""translate_name"",
      ""description"": ""把游戏内部 ID 翻译成游戏官方中文名。当需要向玩家展示物品、建筑、科技的中文名时必须先调用此工具。"",
      ""parameters"": {
        ""type"": ""object"",
        ""properties"": {
          ""kind"": { ""type"": ""string"", ""description"": ""类型：item（物品/建筑）、tech（科技）、recipe（配方）"" },
          ""id"": { ""type"": ""integer"", ""description"": ""游戏内部 ID"" }
        },
        ""required"": [""kind"", ""id""]
      }
    }
  }
]");

        public static async Task<string> ExecuteAsync(string toolName, string argsJson)
        {
            return await RunOnMainThreadAsync(() =>
            {
                try
                {
                    JObject args = string.IsNullOrEmpty(argsJson)
                        ? new JObject()
                        : JObject.Parse(argsJson);

                    switch (toolName)
                    {
                        case "get_player_status":        return GetPlayerStatus();
                        case "get_planet_info":          return GetPlanetInfo(args);
                        case "get_research_status":      return GetResearchStatus();
                        case "get_inventory":            return GetInventory(args);
                        case "get_dyson_sphere_summary": return GetDysonSphereSummary();
                        case "translate_name":           return TranslateName(args);
                        default:
                            return JsonConvert.SerializeObject(new { error = $"未知工具: {toolName}" });
                    }
                }
                catch (Exception ex)
                {
                    AIChatPlugin.Log?.LogWarning($"[AI] 工具 {toolName} 执行失败: {ex.Message}");
                    return JsonConvert.SerializeObject(new { error = ex.Message });
                }
            });
        }

        // ------------------------------------------------------------------

        private static string GetPlayerStatus()
        {
            var player = GameMain.mainPlayer;
            if (player == null)
                return JsonConvert.SerializeObject(new { error = "玩家未加载" });

            var mecha = player.mecha;
            var planet = GameMain.localPlanet;
            var star = GameMain.localStar;

            var data = new
            {
                mecha_energy = mecha?.coreEnergy ?? 0,
                mecha_energy_cap = mecha?.coreEnergyCap ?? 0,
                mecha_hp = mecha?.hp ?? 0,
                mecha_hp_max = mecha?.hpMaxApplied ?? 0,
                star_name = star?.displayName,
                star_id = star?.id ?? -1,
                planet_name = planet?.displayName,
                planet_id = planet?.id ?? -1,
                planet_type = planet?.type.ToString(),
                position = new
                {
                    x = Math.Round(player.position.x, 1),
                    y = Math.Round(player.position.y, 1),
                    z = Math.Round(player.position.z, 1)
                },
                game_tick = GameMain.gameTick
            };
            return JsonConvert.SerializeObject(data);
        }

        private static string GetPlanetInfo(JObject args)
        {
            int planetId = args["planet_id"]?.Value<int>() ?? 0;
            if (planetId <= 0) planetId = GameMain.localPlanet?.id ?? -1;
            if (planetId <= 0)
                return JsonConvert.SerializeObject(new { error = "未指定星球且无当前星球" });

            var planet = GameMain.galaxy?.PlanetById(planetId);
            if (planet == null)
                return JsonConvert.SerializeObject(new { error = $"星球 {planetId} 不存在" });

            var factory = planet.factory;
            if (factory == null)
                return JsonConvert.SerializeObject(new
                {
                    planet_id = planetId,
                    planet_name = planet.displayName,
                    loaded = false,
                    note = "该星球未加载，只能返回未加载状态"
                });

            int entityCount = 0;
            if (factory.entityPool != null)
            {
                for (int i = 1; i < factory.entityPool.Length; i++)
                    if (factory.entityPool[i].id == i) entityCount++;
            }

            int enemyCount = 0;
            if (factory.enemyPool != null)
            {
                for (int i = 1; i < factory.enemyPool.Length; i++)
                    if (factory.enemyPool[i].id == i) enemyCount++;
            }

            var data = new
            {
                planet_id = planetId,
                planet_name = planet.displayName,
                planet_type = planet.type.ToString(),
                loaded = true,
                entity_count = entityCount,
                enemy_count = enemyCount
            };
            return JsonConvert.SerializeObject(data);
        }

        private static string GetResearchStatus()
        {
            var history = GameMain.history;
            if (history == null)
                return JsonConvert.SerializeObject(new { error = "科技数据未加载" });

            var data = new
            {
                current_tech_id = history.currentTech,
                tech_queue_length = history.techQueueLength,
                note = "current_tech_id 需要用 translate_name(kind=tech) 翻译成中文名"
            };
            return JsonConvert.SerializeObject(data);
        }

        private static string GetInventory(JObject args)
        {
            var player = GameMain.mainPlayer;
            if (player?.package == null)
                return JsonConvert.SerializeObject(new { error = "背包未加载" });

            int topN = args["top_n"]?.Value<int>() ?? 20;
            if (topN <= 0) topN = 20;
            if (topN > 50) topN = 50;

            var inv = player.package;
            var list = new System.Collections.Generic.List<Tuple<int, string, int>>();
            int usedSlots = 0;

            try
            {
                var grids = inv.grids;
                if (grids != null)
                {
                    for (int i = 0; i < grids.Length; i++)
                    {
                        var itemId = grids[i].itemId;
                        var count = grids[i].count;
                        if (itemId <= 0 || count <= 0) continue;

                        var proto = LDB.items.Select(itemId);
                        if (proto == null) continue;

                        list.Add(Tuple.Create(itemId, proto.name, count));
                        usedSlots++;
                    }
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = $"读取背包失败: {ex.Message}" });
            }

            var top = list.OrderByDescending(t => t.Item3).Take(topN)
                          .Select(t => new
                          {
                              item_id = t.Item1,
                              name = t.Item2,
                              count = t.Item3
                          })
                          .ToList();

            return JsonConvert.SerializeObject(new
            {
                total_slots_used = usedSlots,
                top_items = top,
                note = "item_id 可用 translate_name(kind=item) 查询中文名和英文名"
            });
        }

        private static string GetDysonSphereSummary()
        {
            var spheres = GameMain.data?.dysonSpheres;
            if (spheres == null)
                return JsonConvert.SerializeObject(new { error = "戴森球数据未加载" });

            int count = 0;
            var list = new System.Collections.Generic.List<object>();
            for (int i = 0; i < spheres.Length; i++)
            {
                var s = spheres[i];
                if (s == null) continue;

                count++;
                var star = GameMain.galaxy?.StarById(i);
                list.Add(new
                {
                    star_id = i,
                    star_name = star?.displayName
                });
            }
            return JsonConvert.SerializeObject(new { total = count, spheres = list });
        }

        // ------------------------------------------------------------------
        // 名词翻译工具
        // ------------------------------------------------------------------

        private static string TranslateName(JObject args)
        {
            try
            {
                string kind = args["kind"]?.ToString()?.ToLowerInvariant() ?? "";
                int id = args["id"]?.Value<int>() ?? 0;
                if (id <= 0)
                    return JsonConvert.SerializeObject(new { error = "无效 ID" });

                switch (kind)
                {
                    case "item":
                        {
                            var proto = LDB.items.Select(id);
                            if (proto == null)
                                return JsonConvert.SerializeObject(new { error = $"物品 {id} 不存在" });
                            return JsonConvert.SerializeObject(new
                            {
                                id = id,
                                name = proto.name,
                                en_name = SafeGetNameEN(proto)
                            });
                        }
                    case "tech":
                        {
                            var proto = LDB.techs.Select(id);
                            if (proto == null)
                                return JsonConvert.SerializeObject(new { error = $"科技 {id} 不存在" });
                            return JsonConvert.SerializeObject(new
                            {
                                id = id,
                                name = proto.name,
                                en_name = SafeGetNameEN(proto)
                            });
                        }
                    case "recipe":
                        {
                            var proto = LDB.recipes.Select(id);
                            if (proto == null)
                                return JsonConvert.SerializeObject(new { error = $"配方 {id} 不存在" });
                            return JsonConvert.SerializeObject(new
                            {
                                id = id,
                                name = proto.name,
                                en_name = SafeGetNameEN(proto)
                            });
                        }
                    default:
                        return JsonConvert.SerializeObject(new { error = $"未知类型: {kind}" });
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }

        /// <summary>
        /// 反射拿 nameEN，避免不同 DSP 版本字段名不同导致编译失败。
        /// </summary>
        private static string SafeGetNameEN(object proto)
        {
            if (proto == null) return "";
            try
            {
                var t = proto.GetType();
                foreach (var name in new[] { "nameEN", "nameEn", "NameEN", "NameEn" })
                {
                    var p = t.GetProperty(name);
                    if (p != null)
                    {
                        var v = p.GetValue(proto) as string;
                        if (!string.IsNullOrEmpty(v)) return v;
                    }
                }
            }
            catch { }
            return "";
        }

        // ------------------------------------------------------------------

        private static Task<string> RunOnMainThreadAsync(Func<string> func)
        {
            var tcs = new TaskCompletionSource<string>();
            MainThreadDispatcher.Enqueue(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }
    }
}